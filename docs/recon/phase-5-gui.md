# Phase 5: GUI

**Status: complete.** The WPF application exposes the validated archive, texture, FCB, reference,
directory-pack, and mesh workflows without bypassing format-library safety gates.

## Acceptance

- Archive paging, search, name discovery, extraction, and adjacent-resource inspection are implemented.
- Texture preview supports every decoded mip and RGBA/RGB/R/G/B/A views.
- PNG/DDS replacement is converted, planned, dry-run verified, staged, and shown as pending before Apply.
- Multi-texture Apply uses one deterministic transaction; Discard and archive switching remove staging files.
- Multi-entry FCB editing uses transaction API v1 for Plan, Dry-run, Copy, Apply, and Discard.
- Directory pack publishes only a new archive pair; resource references use a bounded streaming scanner.
- Mesh preview supports every decoded LOD, orbit, pan, zoom, keyboard control, material references, and FBX export.
- Destructive archive writes require an explicit confirmation and retain rollback until semantic validation passes.

Interactive visual and keyboard checks remain release-checklist items, not implementation gates.
