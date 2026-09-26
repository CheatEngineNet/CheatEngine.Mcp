# Local Cheat Engine Install

This is the shape of the local file an AI agent should create or update after finding the user's Cheat Engine install.

The real `local-cheat-engine.md` is gitignored because it contains machine-specific paths.

Expected fields:

- Cheat Engine path
- CE executable path
- `celua.txt` path
- `celua.txt` hash and last modified time
- `plugins` directory, if found
- `ce.runtimeconfig.json`, if found

Use the recorded `celua.txt` path when writing Cheat Engine Lua for a local install. If no local file exists, find the Cheat Engine install directly or ask the user for their install path.
