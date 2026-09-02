# Fixtures

`Representative` is a source tree with multiple graph islands, duplicate project names, multi-target and conditional dependencies, central package management, a local producer consumed through its package ID, producer ambiguity, an outside-root project reference, and missing/malformed assets cases.

Run `./build-representative.sh` from this directory to create a deterministic local feed, restore the valid projects, and populate authoritative assets files without using a public package source. Generated `bin`, `obj`, and `local-feed` directories are intentionally ignored.
