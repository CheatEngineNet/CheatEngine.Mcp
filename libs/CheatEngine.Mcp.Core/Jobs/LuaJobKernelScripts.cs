namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     The fixed Lua of the one state root that Core owns in Cheat Engine's Lua, <c>__cheatengine_mcp_state</c>: the jobs
///     of every job strategy and the ledger of recorded effects that no Client lease covers.
/// </summary>
/// <remarks>
///     <para>
///         The root holds <c>namespaces[namespace] = {jobs, resources, ...}</c>, one store per plugin activation, and the
///         <c>sweeper</c> timer. The namespace names an activation, so an activation never polls another's jobs, and the
///         next activation sees what an earlier one left as orphans. Ids read <c>kind-namespace-number</c>; the managed
///         side issues them.
///     </para>
///     <para>
///         Jobs buffer items in a ring of <c>bufferLimit</c> slots numbered by a sequence: pushing past the limit evicts
///         the
///         oldest item and counts it in <c>dropped</c>. A poll is read-only and idempotent. A job lives at most its TTL
///         (300 s at most) from creation: the sweeper, a Lua timer that runs once per second while anything can still
///         expire, finishes running jobs as <c>expired</c> through their hooks and removes them. A job whose cleanup
///         failed is retained for manual recovery. No Client call is needed during a disable.
///     </para>
///     <para>
///         Recorded effects (<c>resourceRecord</c>) carry a release function that restores the host once; a release that
///         raises an error keeps the entry with <c>cleanupError</c> until it is acknowledged. Record only releases that
///         restore state, because the ungated release scripts run them.
///     </para>
///     <para>
///         Strategy scripts append their body to <see cref="Strategies" /> and use <c>jobStart</c>, <c>jobPush</c>,
///         <c>jobProgress</c>, <c>jobFinish</c> with <c>jobEvent</c> (Cheat Engine callbacks such as breakpoints),
///         <c>jobSlices</c> (a main-thread timer, at most 20 ms of work per tick every 50 ms or more) or <c>jobThread</c>
///         (a worker thread that hands results back only through <c>synchronize</c>; it needs a live proof before any tool
///         uses it). Nothing here ever waits for a thread or blocks the main thread. Time comes from Cheat Engine's
///         <c>getTickCount</c>, and elapsed time wraps at 2^32.
///     </para>
/// </remarks>
public static class LuaJobKernelScripts
{
	/// <summary>How many activation namespaces the root keeps; a further one refuses new state.</summary>
	public const int MaximumNamespaces = 64;

	/// <summary>How many recorded effects one namespace keeps.</summary>
	public const int MaximumResourcesPerNamespace = 256;

	/// <summary>The longest name or detail of a recorded effect, in bytes.</summary>
	public const int MaximumTextBytes = 256;

	/// <summary>How many entries one snapshot of the root describes.</summary>
	public const int MaximumSnapshotEntries = 1024;

	/// <summary>How many ids one acknowledgement accepts.</summary>
	public const int MaximumAcknowledgedIds = 64;

	/// <summary>The largest ring of one job, in items.</summary>
	public const int MaximumBufferItems = 65536;

	/// <summary>How many items one poll returns at most.</summary>
	public const int MaximumPollItems = 1000;

	/// <summary>The TTL of a job whose caller sets none, in milliseconds.</summary>
	public const int DefaultTimeToLiveMilliseconds = 120000;

	/// <summary>The longest TTL of a job or recorded effect, in milliseconds.</summary>
	public const int MaximumTimeToLiveMilliseconds = 300000;

	/// <summary>How many jobs one namespace retains.</summary>
	public const int MaximumJobsPerNamespace = 64;

	/// <summary>How often the root's timer sweeps expired jobs and effects, in milliseconds.</summary>
	public const int SweepIntervalMilliseconds = 1000;

	/// <summary>The shortest interval of a sliced job's timer, in milliseconds.</summary>
	public const int MinimumSliceIntervalMilliseconds = 50;

	/// <summary>The longest interval of a sliced job's timer, in milliseconds.</summary>
	public const int MaximumSliceIntervalMilliseconds = 60000;

	/// <summary>The most work one tick of a sliced job may do, in milliseconds.</summary>
	public const int MaximumSliceWorkMilliseconds = 20;

	/// <summary>
	///     The kernel's local functions; it references no <c>a[]</c> argument and runs nothing by itself. A fixed script
	///     that records an effect prepends it and calls
	///     <c>resourceRecord(a[1], id, kind, {name, detail, address, size, ttl}, release)</c> with the namespace and an id
	///     from <see cref="Targets.TargetResources.NextId" />, then the tool tracks it with
	///     <see cref="Targets.TargetResources.TrackState" />; <c>resourceForget</c> drops an effect its owner undid.
	/// </summary>
	public const string Kernel = """
	                             local stateMaximumNamespaces = 64
	                             local stateMaximumResources = 256
	                             local stateMaximumText = 256
	                             local stateMaximumSnapshot = 1024
	                             local stateMaximumAcknowledged = 64
	                             local jobMaximumBufferItems = 65536
	                             local jobMaximumPollItems = 1000
	                             local jobMaximumTimeToLive = 300000
	                             local jobMaximumPerNamespace = 64
	                             local jobSweepInterval = 1000

	                             local function stateNow()
	                             	local now = math.tointeger(getTickCount())
	                             	assert(now ~= nil, 'getTickCount did not return an integer tick count')
	                             	return now
	                             end

	                             local function stateElapsed(from, now)
	                             	return (now - from) % 4294967296
	                             end

	                             local function stateCheckNamespace(namespace)
	                             	assert(type(namespace) == 'string' and #namespace >= 1 and #namespace <= 32 and not namespace:find('[^%l%d]'),
	                             		'A state namespace must be 1 to 32 lowercase letters or digits')
	                             end

	                             local function stateCheckKind(kind)
	                             	assert(type(kind) == 'string' and #kind <= 32 and kind:find('^%l[%l%d]*$') ~= nil,
	                             		'A kind must be a lowercase identifier of at most 32 characters')
	                             end

	                             local function stateCheckId(namespace, kind, id)
	                             	stateCheckNamespace(namespace)
	                             	stateCheckKind(kind)
	                             	local prefix = kind .. '-' .. namespace .. '-'
	                             	assert(type(id) == 'string' and #id <= #prefix + 18 and id:sub(1, #prefix) == prefix
	                             		and id:find('^[1-9]%d*$', #prefix + 1) ~= nil, 'An id must read kind-namespace-number')
	                             end

	                             local function stateIdNamespace(id)
	                             	if type(id) ~= 'string' or #id > 80 then return nil end
	                             	return id:match('^%l[%l%d]*%-([%l%d]+)%-[1-9]%d*$')
	                             end

	                             local function stateText(value, name)
	                             	if value == nil then return nil end
	                             	assert(type(value) == 'string' and #value <= stateMaximumText,
	                             		name .. ' must be a string of at most ' .. stateMaximumText .. ' bytes')
	                             	return value
	                             end

	                             local function stateProcessId()
	                             	if type(getOpenedProcessID) ~= 'function' then return nil end
	                             	local read, processId = pcall(getOpenedProcessID)
	                             	if read and math.type(processId) == 'integer' and processId > 0 then return processId end
	                             	return nil
	                             end

	                             local function stateRoot(create)
	                             	local root = rawget(_ENV, '__cheatengine_mcp_state')
	                             	if root == nil and create then
	                             		root = {namespaces = {}, namespaceCount = 0}
	                             		rawset(_ENV, '__cheatengine_mcp_state', root)
	                             	end
	                             	return root
	                             end

	                             local function stateStore(namespace, create)
	                             	stateCheckNamespace(namespace)
	                             	local root = stateRoot(create)
	                             	if root == nil then return nil end
	                             	local store = root.namespaces[namespace]
	                             	if store == nil and create then
	                             		assert(root.namespaceCount < stateMaximumNamespaces, 'At most ' .. stateMaximumNamespaces
	                             			.. ' plugin activations may keep MCP state; release or acknowledge their orphans first')
	                             		store = {jobs = {}, jobCount = 0, resources = {}, resourceCount = 0, order = 0}
	                             		root.namespaces[namespace] = store
	                             		root.namespaceCount = root.namespaceCount + 1
	                             	end
	                             	return store
	                             end

	                             local function stateFind(id)
	                             	local namespace = stateIdNamespace(id)
	                             	local root = stateRoot(false)
	                             	if namespace == nil or root == nil then return nil end
	                             	local store = root.namespaces[namespace]
	                             	if store == nil then return nil end
	                             	return store.jobs[id] or store.resources[id]
	                             end

	                             local function stateRemove(entry)
	                             	local root = stateRoot(false)
	                             	local store = root ~= nil and root.namespaces[entry.namespace] or nil
	                             	if store == nil then return false end
	                             	local entries = entry.family == 'job' and store.jobs or store.resources
	                             	if entries[entry.id] ~= entry then return false end
	                             	entries[entry.id] = nil
	                             	if entry.family == 'job' then
	                             		store.jobCount = store.jobCount - 1
	                             	else
	                             		store.resourceCount = store.resourceCount - 1
	                             	end
	                             	return true
	                             end

	                             -- A running job, a failed cleanup and a recorded effect may still hold state in Cheat Engine or the target.
	                             local function stateHoldsHostState(entry)
	                             	if entry.family == 'job' then return entry.state == 'running' or entry.cleanupError ~= nil end
	                             	return true
	                             end

	                             local function stateDescribe(entry, now)
	                             	local elapsed = stateElapsed(entry.created, now)
	                             	return {id = entry.id, kind = entry.kind, family = entry.family, namespace = entry.namespace,
	                             		state = entry.state, ageMs = elapsed, holdsHostState = stateHoldsHostState(entry),
	                             		expiresInMs = entry.ttl ~= nil and math.max(0, entry.ttl - elapsed) or nil, name = entry.name,
	                             		detail = entry.detail, address = entry.address ~= nil and string.format('%X', entry.address) or nil,
	                             		size = entry.size, processId = entry.processId, cleanupError = entry.cleanupError}
	                             end

	                             -- Every entry that accept(entry) admits, holders first, then newest first.
	                             local function stateCollect(accept)
	                             	local entries = {}
	                             	local root = stateRoot(false)
	                             	if root == nil then return entries, nil end
	                             	local now = stateNow()
	                             	for _, store in pairs(root.namespaces) do
	                             		for _, job in pairs(store.jobs) do
	                             			if accept(job) then entries[#entries + 1] = job end
	                             		end
	                             		for _, entry in pairs(store.resources) do
	                             			if accept(entry) then entries[#entries + 1] = entry end
	                             		end
	                             	end
	                             	table.sort(entries, function(left, right)
	                             		local leftHolds, rightHolds = stateHoldsHostState(left), stateHoldsHostState(right)
	                             		if leftHolds ~= rightHolds then return leftHolds end
	                             		local leftAge, rightAge = stateElapsed(left.created, now), stateElapsed(right.created, now)
	                             		if leftAge ~= rightAge then return leftAge < rightAge end
	                             		if left.namespace ~= right.namespace then return left.namespace < right.namespace end
	                             		return left.order > right.order
	                             	end)
	                             	return entries, now
	                             end

	                             local stateSweep

	                             local function stateStopSweeper(root)
	                             	local sweeper = root.sweeper
	                             	if sweeper == nil then return end
	                             	root.sweeper = nil
	                             	sweeper.timer.Enabled = false
	                             	sweeper.timer.destroy()
	                             end

	                             local function stateEnsureSweeper()
	                             	local root = stateRoot(true)
	                             	if root.sweeper ~= nil then return end
	                             	local timer = createTimer(nil, false)
	                             	assert(timer ~= nil, 'Could not create the MCP state timer')
	                             	local sweeper = {timer = timer}
	                             	local configured, failure = pcall(function()
	                             		timer.Interval = jobSweepInterval
	                             		timer.OnTimer = function()
	                             			local swept, sweepFailure = pcall(stateSweep, nil)
	                             			if not swept then sweeper.error = tostring(sweepFailure) end
	                             		end
	                             		timer.Enabled = true
	                             	end)
	                             	if not configured then
	                             		pcall(function() timer.destroy() end)
	                             		error(failure, 0)
	                             	end
	                             	root.sweeper = sweeper
	                             end

	                             local function jobFind(namespace, id)
	                             	stateCheckNamespace(namespace)
	                             	local entry = stateFind(id)
	                             	if entry == nil or entry.family ~= 'job' or entry.namespace ~= namespace then return nil end
	                             	return entry
	                             end

	                             local function jobCreate(namespace, id, kind, bufferLimit, timeToLive)
	                             	stateCheckId(namespace, kind, id)
	                             	assert(math.type(bufferLimit) == 'integer' and bufferLimit >= 1 and bufferLimit <= jobMaximumBufferItems,
	                             		'bufferLimit must be between 1 and ' .. jobMaximumBufferItems)
	                             	assert(math.type(timeToLive) == 'integer' and timeToLive >= 1 and timeToLive <= jobMaximumTimeToLive,
	                             		'The job TTL must be between 1 and ' .. jobMaximumTimeToLive .. ' milliseconds')
	                             	local now = stateNow()
	                             	assert(stateFind(id) == nil, 'The id ' .. id .. ' is already in use')
	                             	local store = stateStore(namespace, true)
	                             	assert(store.jobCount < jobMaximumPerNamespace,
	                             		'At most ' .. jobMaximumPerNamespace .. ' MCP jobs may be retained; stop finished jobs to release them')
	                             	stateEnsureSweeper()
	                             	store.order = store.order + 1
	                             	local job = {id = id, kind = kind, namespace = namespace, family = 'job', state = 'running', created = now,
	                             		order = store.order, ttl = timeToLive, limit = bufferLimit, ring = {}, first = 1, last = 0, dropped = 0,
	                             		processId = stateProcessId()}
	                             	store.jobs[id] = job
	                             	store.jobCount = store.jobCount + 1
	                             	return job
	                             end

	                             -- O(1): the new item takes the slot of the oldest one once the ring is full.
	                             local function jobPush(job, item)
	                             	assert(item ~= nil, 'A job item cannot be nil')
	                             	if job.state ~= 'running' then return false end
	                             	local sequence = job.last + 1
	                             	job.ring[(sequence - 1) % job.limit + 1] = item
	                             	job.last = sequence
	                             	if sequence - job.first >= job.limit then
	                             		job.first = job.first + 1
	                             		job.dropped = job.dropped + 1
	                             	end
	                             	return true
	                             end

	                             local function jobProgress(job, done, total)
	                             	assert(math.type(done) == 'integer' and done >= 0, 'Job progress must be a non-negative integer')
	                             	assert(total == nil or (math.type(total) == 'integer' and total >= 0),
	                             		'A job total must be a non-negative integer')
	                             	if job.state ~= 'running' then return false end
	                             	job.progressDone = done
	                             	job.progressTotal = total
	                             	return true
	                             end

	                             local function jobStatus(job, now)
	                             	local elapsed = stateElapsed(job.created, now)
	                             	return {jobId = job.id, kind = job.kind, state = job.state, ageMs = elapsed,
	                             		expiresInMs = math.max(0, job.ttl - elapsed), buffered = job.last - job.first + 1, total = job.last,
	                             		dropped = job.dropped, progressDone = job.progressDone, progressTotal = job.progressTotal,
	                             		error = job.error, cleanupError = job.cleanupError, requiresManualRecovery = job.cleanupError ~= nil,
	                             		processId = job.processId}
	                             end

	                             -- Read-only and O(limit): it reads only the returned slots and never moves the cursor.
	                             local function jobPoll(job, afterSequence, limit)
	                             	if math.type(afterSequence) ~= 'integer' or afterSequence < 0 or afterSequence > job.last then
	                             		return {mcp_error = {kind = 'invalid_argument', hostEffect = 'not_started',
	                             			message = 'afterSequence must be between 0 and the last issued sequence, ' .. job.last .. '.',
	                             			hint = 'Pass 0 first, then the nextAfterSequence of the previous poll.'}}
	                             	end
	                             	if math.type(limit) ~= 'integer' or limit < 1 or limit > jobMaximumPollItems then
	                             		return {mcp_error = {kind = 'invalid_argument', hostEffect = 'not_started',
	                             			message = 'limit must be between 1 and ' .. jobMaximumPollItems .. '.'}}
	                             	end
	                             	local start = math.max(afterSequence + 1, job.first)
	                             	local stop = math.min(start + limit - 1, job.last)
	                             	local items = {}
	                             	for sequence = start, stop do
	                             		items[sequence - start + 1] = job.ring[(sequence - 1) % job.limit + 1]
	                             	end
	                             	local nextAfterSequence = stop >= start and stop or afterSequence
	                             	return {job = jobStatus(job, stateNow()), items = items, firstSequence = job.first,
	                             		nextAfterSequence = nextAfterSequence, more = nextAfterSequence < job.last, dropped = job.dropped}
	                             end

	                             -- Idempotent: only the first terminal state counts. The strategy's stop hook runs first, then the owner's
	                             -- onStop; a hook that raises an error records cleanupError and the job stays retained for manual recovery.
	                             local function jobFinish(job, state, message)
	                             	assert(state == 'completed' or state == 'failed' or state == 'stopped' or state == 'expired'
	                             		or state == 'target_changed', 'A job finishes as completed, failed, stopped, expired or target_changed')
	                             	assert(message == nil or type(message) == 'string', 'A job message must be a string')
	                             	if job.state ~= 'running' then return false end
	                             	local now = stateNow()
	                             	job.state = state
	                             	job.finished = now
	                             	if message ~= nil then job.error = message:sub(1, 4096) end
	                             	local failures = {}
	                             	for _, hook in ipairs({job.strategyStop or false, job.onStop or false}) do
	                             		if hook then
	                             			local stopped, failure = pcall(hook, job, state)
	                             			if not stopped then failures[#failures + 1] = tostring(failure) end
	                             		end
	                             	end
	                             	if #failures > 0 then job.cleanupError = table.concat(failures, '; '):sub(1, 4096) end
	                             	return true
	                             end

	                             -- A finished job whose cleanup failed stays retained for manual recovery.
	                             local function jobRelease(job)
	                             	if job.state == 'running' or job.cleanupError ~= nil then return false end
	                             	job.ring = {}
	                             	stateRemove(job)
	                             	return true
	                             end

	                             -- Creates a job and runs setup(job) to arm its strategy and hooks. When setup fails, the job finishes as
	                             -- failed, its hooks run, it is removed unless its cleanup failed too, and the error propagates.
	                             local function jobStart(namespace, id, kind, bufferLimit, timeToLive, setup)
	                             	assert(type(setup) == 'function', 'A job needs a setup function')
	                             	local job = jobCreate(namespace, id, kind, bufferLimit, timeToLive)
	                             	local started, failure = pcall(setup, job)
	                             	if not started then
	                             		jobFinish(job, 'failed', tostring(failure))
	                             		jobRelease(job)
	                             		error(failure, 0)
	                             	end
	                             	return job
	                             end

	                             -- Records an effect of this activation that release(entry, reason) undoes once. fields: name, detail (strings
	                             -- of at most 256 bytes), address, size (integers) and ttl (1 to 300000 ms, released by the sweeper).
	                             local function resourceRecord(namespace, id, kind, fields, release)
	                             	stateCheckId(namespace, kind, id)
	                             	assert(type(release) == 'function', 'A resource needs a release function')
	                             	fields = fields or {}
	                             	assert(type(fields) == 'table', 'Resource fields must be a table')
	                             	local ttl = fields.ttl
	                             	assert(ttl == nil or (math.type(ttl) == 'integer' and ttl >= 1 and ttl <= jobMaximumTimeToLive),
	                             		'A resource TTL must be between 1 and ' .. jobMaximumTimeToLive .. ' milliseconds')
	                             	assert(fields.address == nil or math.type(fields.address) == 'integer', 'A resource address must be an integer')
	                             	assert(fields.size == nil or math.type(fields.size) == 'integer', 'A resource size must be an integer')
	                             	local name = stateText(fields.name, 'name')
	                             	local detail = stateText(fields.detail, 'detail')
	                             	local now = stateNow()
	                             	assert(stateFind(id) == nil, 'The id ' .. id .. ' is already in use')
	                             	local store = stateStore(namespace, true)
	                             	assert(store.resourceCount < stateMaximumResources,
	                             		'At most ' .. stateMaximumResources .. ' MCP resources may be recorded per activation; release some first')
	                             	if ttl ~= nil then stateEnsureSweeper() end
	                             	store.order = store.order + 1
	                             	local entry = {id = id, kind = kind, namespace = namespace, family = 'resource', state = 'active',
	                             		created = now, order = store.order, ttl = ttl, name = name, detail = detail, address = fields.address,
	                             		size = fields.size, processId = stateProcessId(), release = release}
	                             	store.resources[id] = entry
	                             	store.resourceCount = store.resourceCount + 1
	                             	return entry
	                             end

	                             local function resourceFind(namespace, id)
	                             	stateCheckNamespace(namespace)
	                             	local entry = stateFind(id)
	                             	if entry == nil or entry.family ~= 'resource' or entry.namespace ~= namespace then return nil end
	                             	return entry
	                             end

	                             -- Runs the release function once: success removes the entry, and an error keeps it with cleanupError.
	                             local function resourceRelease(entry, reason)
	                             	if entry.cleanupError ~= nil then return false end
	                             	local released, failure = pcall(entry.release, entry, reason)
	                             	if released then
	                             		entry.state = 'released'
	                             		stateRemove(entry)
	                             		return true
	                             	end
	                             	entry.state = 'cleanup_failed'
	                             	entry.cleanupError = tostring(failure):sub(1, 4096)
	                             	return false
	                             end

	                             -- The owner undid the effect itself: the entry is removed without running its release.
	                             local function resourceForget(namespace, id)
	                             	local entry = resourceFind(namespace, id)
	                             	if entry == nil then return false end
	                             	entry.state = 'released'
	                             	return stateRemove(entry)
	                             end

	                             -- The jobs and effects of one kind in every namespace, for process-wide effects such as a speedhack or a pause.
	                             local function stateEntriesOfKind(kind)
	                             	stateCheckKind(kind)
	                             	local found = {}
	                             	local root = stateRoot(false)
	                             	if root == nil then return found end
	                             	for _, store in pairs(root.namespaces) do
	                             		for _, job in pairs(store.jobs) do
	                             			if job.kind == kind then found[#found + 1] = job end
	                             		end
	                             		for _, entry in pairs(store.resources) do
	                             			if entry.kind == kind then found[#found + 1] = entry end
	                             		end
	                             	end
	                             	return found
	                             end

	                             -- namespace: the sweeping activation, or nil from the timer. Only a sweeping activation removes the
	                             -- emptied namespaces of earlier activations, so its own numbering never restarts.
	                             stateSweep = function(namespace)
	                             	if namespace ~= nil then stateCheckNamespace(namespace) end
	                             	local summary = {expired = 0, removed = 0, orphansRemoved = 0, namespacesRemoved = 0, retained = 0,
	                             		orphansRetained = 0, awaitingRecovery = 0, resourcesReleased = 0}
	                             	local root = stateRoot(false)
	                             	if root == nil then return summary end
	                             	local now = stateNow()
	                             	local pending = 0
	                             	for name, store in pairs(root.namespaces) do
	                             		local orphaned = namespace ~= nil and name ~= namespace
	                             		for id, job in pairs(store.jobs) do
	                             			if stateElapsed(job.created, now) >= job.ttl then
	                             				if jobFinish(job, 'expired') then summary.expired = summary.expired + 1 end
	                             				if jobRelease(job) then
	                             					summary.removed = summary.removed + 1
	                             					if orphaned then summary.orphansRemoved = summary.orphansRemoved + 1 end
	                             				end
	                             			end
	                             			if store.jobs[id] ~= nil then
	                             				summary.retained = summary.retained + 1
	                             				if orphaned then summary.orphansRetained = summary.orphansRetained + 1 end
	                             				if job.cleanupError ~= nil then
	                             					summary.awaitingRecovery = summary.awaitingRecovery + 1
	                             				else
	                             					pending = pending + 1
	                             				end
	                             			end
	                             		end
	                             		for _, entry in pairs(store.resources) do
	                             			if entry.ttl ~= nil and entry.cleanupError == nil then
	                             				if stateElapsed(entry.created, now) < entry.ttl then
	                             					pending = pending + 1
	                             				elseif resourceRelease(entry, 'expired') then
	                             					summary.resourcesReleased = summary.resourcesReleased + 1
	                             				end
	                             			end
	                             		end
	                             		if orphaned and store.jobCount == 0 and store.resourceCount == 0 then
	                             			root.namespaces[name] = nil
	                             			root.namespaceCount = root.namespaceCount - 1
	                             			summary.namespacesRemoved = summary.namespacesRemoved + 1
	                             		end
	                             	end
	                             	if pending == 0 then stateStopSweeper(root) end
	                             	return summary
	                             end
	                             """;

	/// <summary>
	///     The kernel plus the three strategies a job can run: <c>jobEvent</c>, <c>jobSlices</c> and <c>jobThread</c>. A start
	///     script appends its body to it and calls <c>jobStart(a[1], a[2], kind, a[3], a[4], setup)</c> with the
	///     arguments of <see cref="JobStart" />, its own arguments following from <c>a[5]</c>.
	/// </summary>
	public const string Strategies = Kernel + "\n" + """
	                                                 local jobMinimumSliceInterval = 50
	                                                 local jobMaximumSliceInterval = 60000
	                                                 local jobMaximumSliceWork = 20

	                                                 -- event: wraps a Cheat Engine callback, such as a breakpoint function. While the job runs, handler(job, ...)
	                                                 -- runs protected and its results are returned; an error finishes the job as failed, which runs its hooks.
	                                                 -- Once the job ended, the wrapper returns idle without running the handler.
	                                                 local function jobEvent(job, handler, idle)
	                                                 	assert(type(handler) == 'function', 'An event job needs a handler')
	                                                 	return function(...)
	                                                 		if job.state ~= 'running' then return idle end
	                                                 		local results = table.pack(pcall(handler, job, ...))
	                                                 		if not results[1] then
	                                                 			jobFinish(job, 'failed', tostring(results[2]))
	                                                 			return idle
	                                                 		end
	                                                 		return table.unpack(results, 2, results.n)
	                                                 	end
	                                                 end

	                                                 -- slices: runs step(job, expired) on Cheat Engine's main thread from a timer every interval milliseconds
	                                                 -- (at least 50). Each tick works at most sliceMs milliseconds (at most 20) by the wrap-safe tick count:
	                                                 -- step returns as soon as expired() is true, and returns true once the job is complete. An error fails it.
	                                                 local function jobSlices(job, step, interval, sliceMs)
	                                                 	assert(type(step) == 'function', 'A sliced job needs a step function')
	                                                 	assert(math.type(interval) == 'integer' and interval >= jobMinimumSliceInterval
	                                                 		and interval <= jobMaximumSliceInterval, 'The slice interval must be between '
	                                                 		.. jobMinimumSliceInterval .. ' and ' .. jobMaximumSliceInterval .. ' milliseconds')
	                                                 	assert(math.type(sliceMs) == 'integer' and sliceMs >= 1 and sliceMs <= jobMaximumSliceWork,
	                                                 		'A slice may work between 1 and ' .. jobMaximumSliceWork .. ' milliseconds')
	                                                 	assert(job.state == 'running' and job.strategyStop == nil, 'A running job takes one strategy')
	                                                 	local timer = createTimer(nil, false)
	                                                 	assert(timer ~= nil, 'Could not create the job timer')
	                                                 	job.slices = 0
	                                                 	job.sliceOverruns = 0
	                                                 	local function stopTimer()
	                                                 		local current = job.timer
	                                                 		if current == nil then return end
	                                                 		job.timer = nil
	                                                 		current.Enabled = false
	                                                 		current.destroy()
	                                                 	end
	                                                 	local function tick()
	                                                 		if job.state ~= 'running' then
	                                                 			stopTimer()
	                                                 			return
	                                                 		end
	                                                 		local started = stateNow()
	                                                 		local function expired()
	                                                 			return stateElapsed(started, stateNow()) >= sliceMs
	                                                 		end
	                                                 		local stepped, done = pcall(step, job, expired)
	                                                 		job.slices = job.slices + 1
	                                                 		if stateElapsed(started, stateNow()) > sliceMs then job.sliceOverruns = job.sliceOverruns + 1 end
	                                                 		if not stepped then
	                                                 			jobFinish(job, 'failed', tostring(done))
	                                                 		elseif done then
	                                                 			jobFinish(job, 'completed')
	                                                 		end
	                                                 	end
	                                                 	job.timer = timer
	                                                 	job.strategyStop = stopTimer
	                                                 	local configured, failure = pcall(function()
	                                                 		timer.Interval = interval
	                                                 		timer.OnTimer = function()
	                                                 			local ticked, tickFailure = pcall(tick)
	                                                 			if not ticked then
	                                                 				pcall(jobFinish, job, 'failed', tostring(tickFailure))
	                                                 				pcall(stopTimer)
	                                                 			end
	                                                 		end
	                                                 		timer.Enabled = true
	                                                 	end)
	                                                 	if not configured then
	                                                 		job.timer = nil
	                                                 		job.strategyStop = nil
	                                                 		pcall(function() timer.destroy() end)
	                                                 		error(failure, 0)
	                                                 	end
	                                                 end

	                                                 -- thread: runs produce(state) on a Cheat Engine worker thread. Each call makes at most one native call and
	                                                 -- returns done, items (a list of plain values), progressDone and progressTotal; it never touches the job or
	                                                 -- any other shared table, only its own state table. Results reach the job only through synchronize, on
	                                                 -- Cheat Engine's main thread, which also tells the worker whether to go on. A stop is cooperative: the worker
	                                                 -- leaves at its next synchronize and nothing ever waits for it. Needs a live proof before any tool uses it.
	                                                 local function jobThread(job, produce)
	                                                 	assert(type(produce) == 'function', 'A threaded job needs a produce function')
	                                                 	assert(job.state == 'running' and job.strategyStop == nil, 'A running job takes one strategy')
	                                                 	local function deliver(ok, done, items, progressDone, progressTotal)
	                                                 		if job.state ~= 'running' then return false end
	                                                 		if not ok then
	                                                 			jobFinish(job, 'failed', tostring(done))
	                                                 			return false
	                                                 		end
	                                                 		if items ~= nil then
	                                                 			assert(type(items) == 'table', 'A worker step returns its items as a list')
	                                                 			for index = 1, #items do jobPush(job, items[index]) end
	                                                 		end
	                                                 		if progressDone ~= nil then jobProgress(job, progressDone, progressTotal) end
	                                                 		if done then
	                                                 			jobFinish(job, 'completed')
	                                                 			return false
	                                                 		end
	                                                 		return true
	                                                 	end
	                                                 	local function synchronized(_, ...)
	                                                 		local delivered, goOn = pcall(deliver, ...)
	                                                 		if not delivered then
	                                                 			pcall(jobFinish, job, 'failed', tostring(goOn))
	                                                 			return false
	                                                 		end
	                                                 		return goOn
	                                                 	end
	                                                 	local name = 'CheatEngine.Mcp ' .. job.id
	                                                 	job.strategyStop = function() job.workerLeaving = true end
	                                                 	local worker = createThread(function(thread)
	                                                 		thread.Name = name
	                                                 		local state = {}
	                                                 		while true do
	                                                 			local goOn = thread.synchronize(synchronized, pcall(produce, state))
	                                                 			if not goOn or thread.Terminated then break end
	                                                 		end
	                                                 	end)
	                                                 	if worker == nil then
	                                                 		job.strategyStop = nil
	                                                 		error('Could not create the job worker thread', 0)
	                                                 	end
	                                                 end
	                                                 """;

	/// <summary>Polls one job of the namespace. Arguments: namespace, job id, <c>afterSequence</c>, <c>limit</c>.</summary>
	internal const string Poll = Kernel + "\n" + """
	                                             local job = jobFind(a[1], a[2])
	                                             if job == nil then
	                                             	return {mcp_error = {kind = 'not_found', hostEffect = 'not_started',
	                                             		message = 'Unknown or expired job: ' .. tostring(a[2]) .. '.',
	                                             		hint = 'Jobs are removed when stopped or when their TTL ends; start a new job.'}}
	                                             end
	                                             return jobPoll(job, a[3], a[4])
	                                             """;

	/// <summary>The status of every job of the namespace. Argument: namespace.</summary>
	internal const string Statuses = Kernel + "\n" + """
	                                                 local store = stateStore(a[1], false)
	                                                 local statuses = {}
	                                                 if store ~= nil then
	                                                 	local now = stateNow()
	                                                 	for _, job in pairs(store.jobs) do statuses[#statuses + 1] = jobStatus(job, now) end
	                                                 end
	                                                 return {jobs = statuses}
	                                                 """;

	/// <summary>Stops one job of the namespace and removes it with its items. Arguments: namespace, job id.</summary>
	internal const string Stop = Kernel + "\n" + """
	                                             local job = jobFind(a[1], a[2])
	                                             if job == nil then return {found = false, wasRunning = false, released = false} end
	                                             local wasRunning = job.state == 'running'
	                                             jobFinish(job, 'stopped')
	                                             local released = jobRelease(job)
	                                             return {found = true, wasRunning = wasRunning, released = released, state = job.state,
	                                             	cleanupError = job.cleanupError}
	                                             """;

	/// <summary>Sweeps every namespace on behalf of one activation. Argument: the activation namespace.</summary>
	internal const string Sweep = Kernel + "\n" + """
	                                              return stateSweep(a[1])
	                                              """;

	/// <summary>Describes every entry of the root, holders first, then newest first, bounded. Argument: namespace.</summary>
	internal const string StateSnapshot = Kernel + "\n" + """
	                                                      stateCheckNamespace(a[1])
	                                                      local entries, now = stateCollect(function() return true end)
	                                                      local described = {}
	                                                      for index = 1, math.min(#entries, stateMaximumSnapshot) do
	                                                      	described[index] = stateDescribe(entries[index], now)
	                                                      end
	                                                      return {entries = described, total = #entries, truncated = #entries > #described}
	                                                      """;

	/// <summary>
	///     Releases, newest first, the entries that hold host state and that no managed handle tracks, stopping at the first
	///     failed cleanup, or only lists them. Arguments: namespace, the tracked ids, whether to release.
	/// </summary>
	internal const string StateReleaseUnmanaged = Kernel + "\n" + """
	                                                              stateCheckNamespace(a[1])
	                                                              assert(type(a[2]) == 'table', 'The tracked ids must be a list')
	                                                              local tracked = {}
	                                                              for _, id in ipairs(a[2]) do tracked[id] = true end
	                                                              local entries, now = stateCollect(function(entry)
	                                                              	return stateHoldsHostState(entry) and not (entry.namespace == a[1] and tracked[entry.id])
	                                                              end)
	                                                              local released = {}
	                                                              local failed
	                                                              if a[3] then
	                                                              	for _, entry in ipairs(entries) do
	                                                              		if entry.cleanupError == nil then
	                                                              			local done
	                                                              			if entry.family == 'job' then
	                                                              				jobFinish(entry, 'stopped')
	                                                              				done = jobRelease(entry)
	                                                              			else
	                                                              				done = resourceRelease(entry, 'released')
	                                                              			end
	                                                              			if not done then
	                                                              				failed = stateDescribe(entry, now)
	                                                              				break
	                                                              			end
	                                                              			released[#released + 1] = stateDescribe(entry, now)
	                                                              		end
	                                                              	end
	                                                              end
	                                                              local remaining = {}
	                                                              for _, entry in ipairs(entries) do
	                                                              	if stateFind(entry.id) == entry and stateHoldsHostState(entry) and #remaining < stateMaximumSnapshot then
	                                                              		remaining[#remaining + 1] = stateDescribe(entry, now)
	                                                              	end
	                                                              end
	                                                              return {released = released, failed = failed, remaining = remaining}
	                                                              """;

	/// <summary>
	///     Removes entries after a manual recovery without running their cleanup; the whole request is refused when an id is
	///     unknown, tracked by this activation or a running job. Arguments: namespace, the ids, the tracked ids.
	/// </summary>
	internal const string StateAcknowledge = Kernel + "\n" + """
	                                                         stateCheckNamespace(a[1])
	                                                         assert(type(a[2]) == 'table' and #a[2] >= 1 and #a[2] <= stateMaximumAcknowledged,
	                                                         	'Acknowledge 1 to ' .. stateMaximumAcknowledged .. ' ids')
	                                                         assert(type(a[3]) == 'table', 'The tracked ids must be a list')
	                                                         local tracked = {}
	                                                         for _, id in ipairs(a[3]) do tracked[id] = true end
	                                                         local entries = {}
	                                                         for index, id in ipairs(a[2]) do
	                                                         	local entry = stateFind(id)
	                                                         	if entry == nil then
	                                                         		return {mcp_error = {kind = 'not_found', hostEffect = 'not_started',
	                                                         			message = 'No MCP state entry has the id ' .. tostring(id) .. '.',
	                                                         			hint = 'List them with runtime_list_resources.'}}
	                                                         	end
	                                                         	if entry.namespace == a[1] and tracked[entry.id] then
	                                                         		return {mcp_error = {kind = 'invalid_state', hostEffect = 'not_started',
	                                                         			message = entry.id .. ' is still tracked by this activation.',
	                                                         			hint = 'Release it with runtime_release_resources.'}}
	                                                         	end
	                                                         	if entry.family == 'job' and entry.state == 'running' then
	                                                         		return {mcp_error = {kind = 'invalid_state', hostEffect = 'not_started',
	                                                         			message = entry.id .. ' is still running.',
	                                                         			hint = 'Release it with runtime_release_resources first.'}}
	                                                         	end
	                                                         	entries[index] = entry
	                                                         end
	                                                         local now = stateNow()
	                                                         local acknowledged = {}
	                                                         for index, entry in ipairs(entries) do
	                                                         	acknowledged[index] = stateDescribe(entry, now)
	                                                         	stateRemove(entry)
	                                                         end
	                                                         return {acknowledged = acknowledged}
	                                                         """;

	/// <summary>Runs the recorded release of one resource of the namespace, once. Arguments: namespace, resource id.</summary>
	internal const string StateReleaseResource = Kernel + "\n" + """
	                                                             local entry = resourceFind(a[1], a[2])
	                                                             if entry == nil then return {found = false, released = false} end
	                                                             local released = resourceRelease(entry, 'released')
	                                                             return {found = true, released = released, cleanupError = entry.cleanupError}
	                                                             """;
}
