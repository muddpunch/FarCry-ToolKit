# Dunia Toolkit — Far Cry 5 Modding Toolkit
### Technical spec / handoff document

This document is written to brief an AI coding assistant (Codex or similar) on the project. It states what's confirmed, what's assumed, and what's still open reverse-engineering work — treat section 5 as unscoped research, not a feature list to implement blind.

---

## 1. Reference project

**github.com/AlphaGlyph1371/WildlandsToolkit** — MIT licensed, C#/.NET 9, WPF. A modding tool for Tom Clancy's Ghost Recon Wildlands `.forge` archives.

Solution layout:
- `Wildlands.Formats` — format library, no UI dependency
- `Wildlands.Toolkit` — WPF GUI application
- `Wildlands.Cli` — command-line tool

Features/patterns to replicate **conceptually** in the new project:
- Archive browser: open every archive, list entries, search across neighbouring archives for a resource
- Texture viewer: per-mip inspection (incl. streamed mips), channel toggles, export to PNG/DDS/etc., **replace** with automatic re-encode unless the input already matches target format/size
- Mesh viewer: WPF 3D viewport, orbit/pan/zoom, export to binary FBX with skeleton/skin weights
- Material inspector: resolved parameter names + referenced textures
- Weather/time-of-day editor: curve editor with control points, editable as raw text round-trip
- CLI with verbs like `list`, `entry`, `get`, `tex`, `mips`, `pack`, `rebuild`, `refs`, `hash`
- Safety model: before the *first* write to an archive, copy it to `<archive>.original` (never touched again); nothing is written until the user presses **Apply**; a **Discard** option throws away pending changes

**Do not reuse Wildlands' actual parsing code.** Wildlands uses `.forge` archives — a different, unrelated format from Dunia's `.fat`/`.dat`. Only the *architecture and UX patterns* carry over.

---

## 2. Target

- **Game:** Far Cry 5 (Ubisoft Montreal/Toronto, 2018). Same engine family also covers Far Cry New Dawn, and with variation FC3/FC4/Primal/FC6.
- **Engine:** Dunia 2
- **Scope:** offline, single-player/co-op game files only. No live-service or anti-cheat system is touched by this project — it edits local install files, same category as any other single-player asset-modding tool.

---

## 3. Solution layout

```
DuniaToolkit.slnx
/Dunia.Formats/    archive (.fat/.dat), FCB, texture (.xbt) codecs — no UI deps
/Dunia.Toolkit/    WPF GUI
/Dunia.Cli/        console tool, verb surface mirrors Wildlands.Cli
```

---

## 4. Known file formats — build on existing community work, don't re-derive from zero

### 4.1 Archive: `.fat` / `.dat` pairs
- `.fat` = index/header, `.dat` = payload blob. Same archive family across FC3→FC6 (LZ4-based compression by the FC5 era).
- Multiple `.dat/.fat` pairs per install, one per subsystem — e.g. `common.dat/fat` (menus/HUD), `ige.dat/fat` (in-game editor). Full filename catalogue documented at mods.farcry.info/fc5 — pull that list rather than guessing names.
- **Primary permissive reference implementation:** **github.com/gibbed/Gibbed.Dunia** is zlib-licensed and contains explicit Far Cry 5/New Dawn/6 projects plus FAT v10/v11 serializers, compression mappings, hashing, unpacking, and packing code.
- **Legacy permissive reference:** **github.com/gibbed/Gibbed.Dunia2** is also zlib-licensed but its archive implementation stops at v9.
- **GPL reference implementation:** **github.com/JakubMarecek/FCBConverter** extends the Gibbed lineage for FC5/FCND, but the repository is GPLv3. Treat it as a behavioral oracle only unless this toolkit intentionally adopts GPLv3; do not copy its source or bundled data into a permissively licensed build.
- Secondary reference: **github.com/JakubMarecek/FC5ArchiveViewer** — browses FC5/New Dawn `.dat/.fat` without unpacking. **License is GPLv3** (copyleft) — unlike the Gibbed code above, don't lift code verbatim from this one if the new toolkit is meant to ship under a permissive license. Fine to read for structural understanding, re-implement independently.
- No-source reference binaries (useful to generate ground-truth unpacked output to diff your own parser against): "Far Cry 5/New Dawn .dat/.fat Unpack/Repack Tool" and the Ekey FC5 FAT/DAT tool, both circulated on ZenHAX/ResHax.

### 4.2 FCB — "FarCryBinary"
- Binary tree format for entities/archetypes/config values. Tags are hash-keyed, not string-named — same "hash → known name" problem Wildlands solved for its own `.forge` format.
- Reference: **github.com/JakubMarecek/FCBConverter** — full GPLv3 C# source, maintained against FC5 through FC6, and ships a large filename/hash list. Do not redistribute that list until its licensing/provenance is reviewed.
- Where it lives: `entityarchetypelibrary\*.ARK.FCB` = archetypes (vehicle, enemy soldier, prop — behavior + model + texture + sound bindings in one record).

### 4.3 Textures: `.xbt`
- DDS wrapped in a small custom header. Manual conversion is documented (strip everything before the `DDS` magic bytes, the rest is a standard DDS you can open in any DDS-capable editor). FCBConverter automates this both directions (its changelog lists a "PNG2XBT" feature, i.e. confirmed round-trip).
- Implementation: `.xbt` → strip header → decode via any standard DDS library → PNG/BMP export. Reverse path for replace.

### 4.4 Data folder layout
- Install root holds multiple `.dat/.fat` pairs by subsystem (§4.1). Don't hardcode a partial list — pull the documented one from mods.farcry.info/fc5 at implementation time.

---

## 5. NOT documented — open reverse-engineering, not a spec to implement

Flag these explicitly to whoever/whatever implements this — they're research spikes with unknown effort, not scoped features.

- **Mesh containers.** No public write-up found for FC5 specifically (unlike archive/FCB/texture, which the community has solved repeatedly since FC3). Plan: pull candidate mesh resources through the working archive layer, diff headers across several meshes by hand, look for any Dunia-family mesh tooling that might have surfaced since. Don't promise a 3D viewer or FBX export on a fixed timeline until this spike reports back.
- **Materials / texture sets.** Likely FCB entries referencing `.xbt` resources by hash. Confirm the actual shape empirically once the FCB reader is working — don't assume Wildlands' material model (built for a different engine) transfers.
- **Weather / time-of-day.** Wildlands exposes `TimeOfDayPropertyControllerData` / `WeatherPropertyControllerData` as editable curves in its own format. No confirmed FC5 equivalent exists in public docs. `ige.dat/fat` (in-game editor archive) is the most likely place to look once FCB dumps are readable — treat as unconfirmed until then.

---

## 6. Build phases

0. **Recon.** Unpack the same test archive with FCBConverter *and* one Ekey/ZenHAX binary tool. Diff the outputs. Confirm the byte layout is understood before a single line of your own parser is written.
1. **Archive layer.** `.fat/.dat` reader + writer. `<archive>.original` backup-before-first-write, same pattern as Wildlands.
2. **FCB layer.** Tree parser, hash→name resolution seeded from FCBConverter's list. Round-trip test: read → write → byte-diff against the original source file == 0.
3. **Texture layer.** `.xbt` ↔ DDS, PNG export/import.
4. **CLI.** Mirror the Wildlands.Cli verb surface (`list`, `entry`, `get`, `tex`, `pack`, `rebuild`, `refs`, `hash`), adapted to Big/FCB. This is where format understanding gets validated before any UI is built on top of it.
5. **GUI.** Archive browser, texture viewer + replace with pending-changes/Apply/Discard, FCB/entity inspector.
6. **Stretch — gated on its own Phase-0-style recon.** Mesh viewer + FBX export, weather/time-of-day curve editor.

---

## 7. Prior art — reference table

| Project | Gives you | License | URL |
|---|---|---|---|
| WildlandsToolkit | Architecture + UX to mirror (not format code) | MIT | github.com/AlphaGlyph1371/WildlandsToolkit |
| Gibbed.Dunia2 | Original Dunia2 archive/FCB format code | zlib-style, permissive | github.com/gibbed/Gibbed.Dunia2 |
| Gibbed.Dunia | Current FC5/New Dawn/FC6 FAT serializers, hashing, pack/unpack code | zlib-style, permissive | github.com/gibbed/Gibbed.Dunia |
| FCBConverter | FC5/New Dawn/FC6 archive + FCB + texture conversion; behavioral oracle only | **GPLv3 — copyleft, don't copy verbatim or bundle its data** | github.com/JakubMarecek/FCBConverter |
| FC5ArchiveViewer | FC5/New Dawn archive browsing reference | **GPLv3 — copyleft, don't copy verbatim** | github.com/JakubMarecek/FC5ArchiveViewer |
| mods.farcry.info/fc5 | Data-folder/file catalogue, modding basics | community wiki | mods.farcry.info/fc5 |
| ZenHAX / ResHax threads | No-source unpack/repack binaries for ground-truth diffing | binary-only tools | search "Far Cry 5 dat fat" on zenhax.com / reshax.com |

---

## 8. Tech stack

C#/.NET 9, WPF — matches the reference project, keeps the UX patterns (3D viewport, pending-changes model) directly portable, and matches the ecosystem the existing FC5 tooling (FCBConverter, FC5ArchiveViewer) is already written in.

---

## 9. Safety pattern

Identical to Wildlands: backup-on-first-write (`<archive>.original`, never overwritten again), nothing committed to disk until an explicit **Apply**, a visible pending-change list before that commit, **Discard** to throw it away.

---

## 10. Testing strategy

No independent format spec exists for any of these formats — round-trip byte-diff tests (read → write → compare to source, expect zero diff) are the primary correctness signal for every layer: archive, FCB, texture. Treat any round-trip that doesn't diff clean as a parser bug, not a "close enough."
