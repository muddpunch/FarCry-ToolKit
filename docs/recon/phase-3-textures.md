# Phase 3: texture recon

## DDS import — 2026-09-09

`XbtDdsImporter` validates the `TBX\0` wrapper and its declared DDS offset, preserves the complete wrapper byte-for-byte, and replaces only the embedded DDS. Import is rejected before writing when dimensions, depth, mip count, pixel format, DX10 resource layout, cubemap flags, or total DDS length differ from the template.

`tex import <template.xbt> <replacement.dds> <output.xbt>` publishes through a same-directory temporary file and never overwrites an existing destination.

Real `common.fat` entry 1087 (`graphics\_common\_textures\generic\mask\blackarray4x4x1.xbt`) uses a 36-byte XBT wrapper and a 172-byte DDS payload. Extracting and reimporting that DDS reproduced the complete 208-byte XBT byte-for-byte with SHA-256 `9CFA5F26F01F5EA24FCD5EE60E1288DF8AEA94BB92EBC971B3EB4D023E89C6D9`. A 96×96 BC3 DDS was rejected against the 4×4 BC1-array template before output publication.

## Next gate

Implement PNG decoding and template-driven DDS encoding for the validated FC5 DXGI formats.
