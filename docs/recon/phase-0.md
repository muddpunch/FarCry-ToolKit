# Phase 0: archive recon

## Confirmed

- Dunia archive indexes use a FAT/DAT pair.
- The original zlib-licensed Gibbed implementation identifies the format with `FAT2`, exposes `Big.Entry` and `Big.SubFatEntry`, and supports archive versions through v9.
- FCBConverter documentation identifies v9 with FC3/FC4 and v10 with FC5/FCND.
- FCBConverter and FC5ArchiveViewer are GPLv3. Their code and bundled name/hash data must not be copied into a permissive toolkit.

## Evidence still required

- Unmodified FC5 `.fat/.dat` fixture pairs, including one empty 32-byte FAT and one archive containing compressed entries.
- Extraction manifests and SHA-256 hashes from FCBConverter and an independent Ekey/ZenHAX tool for the same fixture.
- Hex/field annotation for the v10 header and every v10 entry variant.
- Compression framing confirmation for uncompressed and LZ4 entries.

## Acceptance gate

Archive parsing starts only after two independent tools produce matching extracted byte hashes for the selected fixtures. Fixtures derived from game files stay local and are never committed.

## Sources

- https://github.com/gibbed/Gibbed.Dunia2
- https://github.com/JakubMarecek/FCBConverter
- https://github.com/JakubMarecek/FC5ArchiveViewer
- https://downloads.fcmodding.com/others/fcbconverter/

