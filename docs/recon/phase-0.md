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

## Local FC5 validation — 2026-08-22

Read-only inspection of a Polish Ubisoft Connect installation confirmed the following across all 16 shipped `.fat` files:

- Raw signature bytes: `32 54 41 46` (`2TAF` on disk, canonical little-endian value `FAT2`).
- Version: `10`.
- Platform value: `1`.
- Header size: 24 bytes.
- SubFAT entry count and SubFAT count: both zero.
- Entry size: exactly 20 bytes.
- Trailer: 8 zero bytes.
- Observed entry-count range: 0 through 292,148.
- `24 + entryCount * 20 + 8` matched the exact file length for every index.

Local fixture fingerprints:

| Fixture | Size | Entries | SHA-256 |
|---|---:|---:|---|
| `patch_english_feminine.fat` | 32 | 0 | `DAB00650D43859A070B58451C119B283A8C495D095DD21E501DE671165EF3D91` |
| `common.fat` | 68,052 | 3,401 | `7BFF1EF1FA6175F36DBC2546A1FFE5B499DEC70845879472686AE359E72DDDD8` |

Full metadata decoding of `common.fat` produced 3,198 LZ4 entries, 203 uncompressed entries, zero encrypted entries, and zero payload ranges outside `common.dat`.

No game-derived bytes are stored in this repository.

## Sources

- https://github.com/gibbed/Gibbed.Dunia2
- https://github.com/gibbed/Gibbed.Dunia
- https://github.com/JakubMarecek/FCBConverter
- https://github.com/JakubMarecek/FC5ArchiveViewer
- https://downloads.fcmodding.com/others/fcbconverter/
