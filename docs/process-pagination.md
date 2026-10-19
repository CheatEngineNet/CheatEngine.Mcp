# Stable process-list pagination

`process_list` keeps its existing unpaged behavior when `pageSize` and `cursor` are both omitted. Its response then has
only `processes` and `truncated`, and `maximumResults` and `nameContains` retain their existing meanings.

Set `pageSize` (1 through 512) on an initial call to opt into paging, leaving `maximumResults` at its legacy default of
1024. MCP asks Cheat Engine for at most 4096 matching processes once, sorts that captured list by process id and then
ordinal process name, and returns the first page. The
response includes `nextCursor` while another captured page exists, `snapshotTotal`, and `snapshotTruncated`. `truncated`
is true when another page exists or Cheat Engine reported that the 4096-process capture omitted further matches.

Pass only `cursor` on later calls. A continuation must keep the legacy `maximumResults` default of 1024 and omit both
`nameContains` and `pageSize`; it reads the original snapshot and never enumerates processes again. A record remains in
that snapshot after its process exits. Processes created after the initial call appear only in a fresh initial listing.
Every attachment still requires an explicit current id or exact name.

Cursors are opaque, scoped to one backend activation, and replayable until their original 30-second expiry. The activation
retains at most four snapshots, each containing at most 4096 process summaries; a fifth initial paged listing evicts the
oldest snapshot. Unknown, malformed, evicted, expired, or other-activation cursors fail with `invalid_state`; start a
fresh paged listing in that case.
