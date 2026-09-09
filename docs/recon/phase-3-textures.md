# Phase 3: texture recon

## DDS import — 2026-09-09

`XbtDdsImporter` validates the `TBX\0` wrapper and its declared DDS offset, preserves the complete wrapper byte-for-byte, and replaces only the embedded DDS. Import is rejected before writing when dimensions, depth, mip count, pixel format, DX10 resource layout, cubemap flags, or total DDS length differ from the template.

`tex import <template.xbt> <replacement.dds> <output.xbt>` publishes through a same-directory temporary file and never overwrites an existing destination.

Real `common.fat` entry 1087 (`graphics\_common\_textures\generic\mask\blackarray4x4x1.xbt`) uses a 36-byte XBT wrapper and a 172-byte DDS payload. Extracting and reimporting that DDS reproduced the complete 208-byte XBT byte-for-byte with SHA-256 `9CFA5F26F01F5EA24FCD5EE60E1288DF8AEA94BB92EBC971B3EB4D023E89C6D9`. A 96×96 BC3 DDS was rejected against the 4×4 BC1-array template before output publication.

## PNG export and template-driven import — 2026-09-09

`tex export-png` decodes the embedded DDS and emits a CRC-protected 8-bit RGBA PNG. `tex import-png` validates PNG structure, chunk CRCs, dimensions, color layout, filters, and decompressed bounds, then regenerates the template mip count and compression format. Import currently accepts non-array 2D DX10 BC1, BC2, BC3, BC4, BC5, and BC7 templates; unsupported arrays, cubemaps, volumes, HDR formats, and interlaced or non-8-bit PNG inputs are rejected before output publication.

Real `common.fat` entry 40 (`ui\zeta\06_icons\worldmapcompass\tx_animal_fish_salmon.xbt`) completed `XBT → PNG → DDS → XBT → PNG`. Both XBT files were 9,400 bytes, retained the exact 36-byte wrapper, resolved as 96×96 BC3 with one mip, and decoded successfully after re-encoding.

The WPF texture preview exposes atomic **Export PNG**, **Create replacement XBT**, and **Replace in archive** actions. PNG and DDS replacements use the same validated format-library paths as the CLI and preserve the original wrapper.

## Archive transaction — 2026-09-09

`XbtArchiveReplacementService` is the shared Plan, Dry-run, Copy, and Apply boundary. Its SHA-256 plan binds the entry index, expected resource hash, source payload hash, replacement payload hash, and replacement length. Dry-run rebuilds a temporary pair, verifies the exact replacement bytes, decodes the published XBT, and removes all temporary outputs.

Copy refuses existing destinations and removes both outputs if post-publication texture validation fails. Apply fingerprints and locks the source pair, revalidates the planned source payload before building, creates immutable `.original` backups, and retains rollback files until both byte-exact replacement validation and XBT decoding succeed.

The WPF action converts PNG or DDS input into a template-compatible XBT in memory, executes Plan and Dry-run, displays the bound plan hash, requires an explicit warning confirmation, then calls the transaction facade. The CLI exposes the same boundary through `tex archive-plan`, `tex archive-dry-run`, `tex archive-copy`, and confirmed `tex archive-apply`.

## Mip and channel inspection — 2026-09-09

`XbtMipDecoder` decodes the complete DDS mip chain with 256 MiB encoded and 64-megapixel decoded safety limits. `mips list` reports every level and `mips export` atomically publishes a new directory containing one CRC-protected RGBA PNG per mip.

The WPF preview now selects individual mip levels and isolates RGBA, opaque RGB, red, green, blue, or alpha output without re-decoding the source texture. Channel-only views render as opaque grayscale so mask data remains directly comparable.

The real `common.fat` entry 40 smoke test decoded one 96×96 mip, exported `mip-00-96x96.png` at 5,253 bytes, and removed its temporary extraction/export directory after verification.

## Next gate

Validate a copied archive with a visibly modified texture in-game, then record the exact build, archive, entry, and observed render result.
