# Investigation notes

`investigation_create_note`, `investigation_search_notes`, `investigation_update_note`, and
`investigation_export_notes` keep durable investigation evidence outside the plugin's own data directory.
Choose an existing ordinary local directory below `Mcp:Files:AllowedRoots` to create or update notes.
Search needs only a readable notes directory, so saved revisions can still be inspected after the write-root policy
changes. Exports are separate envelopes for other readers; search reads canonical revision files.

A name is 1–64 ASCII letters, digits, `_`, or `-`. It becomes lower case. For example, `Player_State` is
`player_state`, and its first immutable revision is:

```text
cemcp-note-player_state.r0000000001.json
```

Every update supplies the latest `expectedRevision` and creates the next file; it never replaces an old file.
Operations on the same canonical directory coordinate across local plugin instances. If two updates use the same
revision, one succeeds and the other receives an `invalid_state` conflict. Publication also refuses overwrites at
the filesystem boundary. A missing revision, duplicate revision, renamed revision, extra or duplicate JSON
property, malformed content, unknown schema, or future version stops reads rather than attempting migration.
Directories are capped at 4096 canonical revision files and 16 MiB of revision content, including history. Each
revision is capped at 256 KiB; exports are capped at 16 MiB. Writes that would cross either directory limit are
refused. A busy directory wait is bounded to five seconds and supports cancellation.

Search returns only the latest revision for each name, ordered ordinally by canonical name. `offset` is zero based
and `limit` is 1–100. A single call reads one current view; a later page request is deliberately a new read and can
reflect writes made in between.

Titles are nonempty and at most 256 characters; bodies are at most 16384. There can be up to 16 nonempty 64-character
tags and 32 nonempty 1024-character observations. `confidence` is omitted or one of `low`, `medium`, and `high`.

Notes may store a historical module-build identity and hexadecimal RVA. The stored binding intentionally contains no
current process address. Creating a binding requires both `module` and `rva`; an update retains and validates an old
binding when those arguments are omitted, can replace it with both arguments, or clears it only with `clearBinding`.
Search and export are historical by default. Set `validateBinding` when an attached target must prove the saved
build still matches. Search validates only the returned page; export validation is limited to 100 bound notes, so
narrow the filters for a larger notebook. The identity fingerprints PE build metadata, not the complete module bytes.

Export writes every matching latest note (it is not limited by search paging) as:

```json
{ "schema": "cemcp.investigation-export", "version": 1, "notes": [] }
```

The destination must be an allowed file and cannot be a canonical note revision or an alias of one. `overwrite` is
false by default. Results return only the output file name, never a host path.
