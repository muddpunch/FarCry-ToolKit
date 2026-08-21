# Dunia Toolkit

Work-in-progress modding toolkit for the PC version of **Far Cry 5**, built with C#/.NET 9 and WPF.

The project aims to provide one safe application for browsing, extracting, inspecting, replacing, and rebuilding local Dunia 2 game resources. It targets offline game files only and does not interact with multiplayer services, anti-cheat, process memory, or a running game.

> [!WARNING]
> Archive parsing and writing are not ready for production use. Do not use the current build to modify a game installation.

## What it will do

- Browse Far Cry 5 `.fat`/`.dat` archive pairs without unpacking the entire game.
- Search for resources across related archives.
- Extract individual files or complete archives.
- Inspect and round-trip FCB (`FarCryBinary`) trees with hash-to-name resolution.
- Preview `.xbt` textures and export their embedded DDS data.
- Replace textures with automatic validation and conversion.
- Stage modifications in memory before writing anything to disk.
- Show every pending change and support explicit **Apply** or **Discard** actions.
- Create permanent `.original` backups before the first archive write.
- Expose the same core operations through a WPF application and CLI.

Mesh viewing, FBX export, materials, weather, and time-of-day editing are research targets. They are not promised features until their FC5 formats are understood and verified.

## How Dunia archives work

Far Cry 5 stores most resources in archive pairs:

- `.fat` contains the archive index and metadata.
- `.dat` contains the resource payloads.
- File paths are commonly represented by hashes.
- Payloads may be stored uncompressed or compressed, including LZ4-era variants.
- FC5 and Far Cry New Dawn use FAT format version 10.

The toolkit keeps format handling separate from presentation and applies changes through a staged transaction:

```mermaid
flowchart LR
    A[Open FAT/DAT pair] --> B[Parse index]
    B --> C[Resolve and inspect resources]
    C --> D[Stage replacements]
    D --> E{User action}
    E -->|Discard| F[Drop pending changes]
    E -->|Apply| G[Create .original backups]
    G --> H[Write verified archive output]
```

No write should occur while browsing, extracting, previewing, or staging a replacement.

## Safety model

Before the first write to an archive file, the toolkit creates:

```text
patch.fat          -> patch.fat.original
patch.dat          -> patch.dat.original
```

An existing `.original` file is never overwritten. Backup publication is atomic, concurrent calls create exactly one backup, and temporary files are removed after success, failure, or cancellation.

Writes will remain disabled until FAT v10 parsing and rebuilding pass byte-exact round-trip tests against real FC5 fixtures.

## Current status

| Area | Status |
|---|---|
| .NET 9 solution and project boundaries | Implemented |
| FAT prefix recon probe | Implemented |
| Validated FAT v10 envelope/entry-count reader | Implemented |
| FAT v10 entry metadata reader | Implemented |
| Atomic `.original` backup primitive | Implemented |
| Complete FAT/DAT pair backup preflight | Implemented |
| Pending-change set with Apply/Discard foundations | Implemented |
| SHA-256 verified replacement staging | Implemented |
| FAT v10 entry parser | Blocked on fixture-backed recon |
| FAT/DAT extraction and rebuilding | Not implemented |
| FCB parser and writer | Not implemented |
| XBT → DDS extraction | Implemented |
| DDS/PNG → XBT import and re-encode | Not implemented |
| CLI `probe` and `tex extract` commands | Implemented |
| Remaining production CLI commands | Not implemented |
| WPF archive browser | Not implemented |

The authoritative technical handoff is in [`dunia-toolkit-fc5-spec.md`](dunia-toolkit-fc5-spec.md). Recon findings and acceptance gates are tracked in [`docs/recon/phase-0.md`](docs/recon/phase-0.md).

## Solution layout

```text
DuniaToolkit.slnx
├── Dunia.Formats/        FAT/DAT, FCB, hashing, and texture codecs; no UI dependencies
├── Dunia.Formats.Tests/  Format, round-trip, concurrency, and safety tests
├── Dunia.Cli/            Command-line interface and recon utilities
└── Dunia.Toolkit/        Windows WPF application
```

`Dunia.Formats` must remain independent of WPF so the same verified implementation is used by the GUI, CLI, and tests.

## Requirements

- Windows 10 or newer
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or a newer SDK capable of targeting .NET 9

## Build and test

```powershell
dotnet build DuniaToolkit.slnx --configuration Release
dotnet test DuniaToolkit.slnx --configuration Release
```

Warnings are treated as errors.

## Implemented library surface

- `ArchiveBackupService` atomically creates one permanent `.original` backup.
- `ArchivePairBackupService` validates and backs up a complete FAT/DAT pair.
- `PendingChangeSet<TKey, TChange>` provides ordered, thread-safe staging and discard snapshots.
- `ReplacementStagingStore` snapshots replacement files and verifies their length and SHA-256 before use.
- `FatPrefixProbe` reports raw archive prefix evidence without assuming an unverified FAT v10 layout.
- `FatV10IndexSummaryReader` validates the confirmed 24-byte header, 20-byte entry envelope, and 8-byte trailer.
- `FatV10IndexReader` decodes hashes, sizes, offsets, encryption flags, and LZ4/none metadata with paired-DAT bounds checks.
- `XbtDdsExtractor` strips the XBT wrapper and streams the embedded DDS payload to an output stream.

## Recon probe

The only working CLI operation currently reads a small prefix from a `.fat` file. It reports raw bytes and both possible byte orders without claiming that the remaining FAT v10 layout is understood.

```powershell
dotnet run --project Dunia.Cli -- probe "D:\Games\Far Cry 5\data_final\pc\patch.fat"
```

Example output shape:

```text
path=D:\Games\Far Cry 5\data_final\pc\patch.fat
length=...
magic.ascii=...
version.le=...
version.be=...
prefix.hex=...
```

Extract a DDS payload without overwriting an existing output file:

```powershell
dotnet run --project Dunia.Cli -- tex extract "input.xbt" "output.dds"
```

Planned CLI verbs are `list`, `entry`, `get`, `pack`, `rebuild`, `refs`, and `hash`. They are displayed in help output but intentionally return an unavailable-command error until implemented and tested.

## Correctness requirements

There is no complete independent FC5 format specification. Correctness therefore depends on fixture-based verification:

1. Extract the same unmodified archive with two independent established tools.
2. Compare every extracted file by SHA-256, not only by name or size.
3. Annotate the observed FAT v10 fields and compression framing.
4. Implement the smallest confirmed reader surface.
5. Require `read -> write -> byte comparison` to produce zero differences.
6. Enable archive writes only after round-trip and failure-path tests pass.

Game-derived fixtures must remain local. Do not commit Ubisoft assets, extracted resources, or proprietary reference binaries.

## Prior art and licensing

- [Gibbed.Dunia](https://github.com/gibbed/Gibbed.Dunia) is zlib-licensed and provides explicit FAT v10/v11 serializers plus FC5 pack/unpack projects.
- [Gibbed.Dunia2](https://github.com/gibbed/Gibbed.Dunia2) is the legacy zlib-licensed baseline through FAT v9.
- [FCBConverter](https://github.com/JakubMarecek/FCBConverter) supports later Far Cry games but is GPLv3. Treat it as a behavioral oracle unless this project explicitly adopts GPLv3.
- [FC5ArchiveViewer](https://github.com/JakubMarecek/FC5ArchiveViewer) is GPLv3 and must not be copied into a permissively licensed implementation.
- [WildlandsToolkit](https://github.com/AlphaGlyph1371/WildlandsToolkit) provides architecture and UX inspiration only; its `.forge` parsing code is unrelated to Dunia FAT/DAT archives.

The repository does not currently contain a project license. Add one before distributing binaries or accepting substantial third-party contributions.

## Disclaimer

This is an unofficial community project and is not affiliated with or endorsed by Ubisoft. Keep verified backups of original game files and use modifications at your own risk.
