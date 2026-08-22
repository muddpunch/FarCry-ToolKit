# Phase 2: FCB recon

## Confirmed layout

- Signature constant: `0x4643626E` (`6E 62 43 46` on disk).
- Version: `2`.
- Flags: `0` for the inspected FC5 fixture.
- Header size: 16 bytes.
- Header stores two declared counters whose exact semantics are not yet confirmed.
- Nodes contain packed child counts, a 32-bit type hash, packed field counts, and raw hash-keyed byte values.
- Packed values below `0xFE` occupy one byte; `0xFF` introduces a 32-bit inline count; `0xFE` introduces a 32-bit backward-reference/pointer value.
- Child pointers reference previously materialized nodes by pointer-table index.
- Field references target the packed-size position of an earlier inline value.

The structural implementation is independently derived from the zlib-licensed `BinaryResourceFile` in `Gibbed.Dunia`. GPLv3 FCBConverter code is not copied.

## Local FC5 validation — 2026-08-22

Read-only signature scanning of `common.fat/common.dat` found 10 FCB v2 resources within the first 291 eligible entries. Both uncompressed and LZ4 payloads were detected.

Entry 93 was extracted temporarily and parsed successfully:

| Property | Value |
|---|---:|
| FAT name hash | `0514813338C00498` |
| Payload size | 120 bytes |
| Compression | none |
| FCB version | 2 |
| Flags | 0 |
| Declared objects | 2 |
| Declared values | 1 |
| Parsed unique nodes | 2 |
| Parsed fields | 6 |
| Root type hash | `E7046466` |

The declared counters do not directly equal the parser's unique-node and field totals, so the writer preserves them verbatim until their semantics are confirmed.

## Writer validation — 2026-08-22

`FcbWriter` emits canonical packed counts and preserves graph identity for backward value references and shared child pointers. Synthetic fixtures cover both reference forms and pass byte-exact round-trips.

Two temporary game-derived fixtures from `common.fat` passed `read -> write -> SHA-256` equality:

| Entry | FAT name hash | Stored form | Length | SHA-256 |
|---:|---:|---|---:|---|
| 93 | `0514813338C00498` | none | 120 | `B499881AD3C7E7DA3DD846CBEAABAF7C7EAD094573196B3FB4285B8EE7378CAD` |
| 98 | `0538FAFBC89D9E17` | LZ4 | 1,291 | `69067BF648B258F08E9CC86802F93D5A0BA3FB0AF8381281D4501B7ACD570C06` |

Both temporary fixtures were deleted and are not stored in the repository.

## Next gate

## CRC32 name recovery — 2026-08-22

FCB type and field names use case-sensitive CRC-32/ISO-HDLC (`poly=EDB88320`, `init/xorout=FFFFFFFF`). Plain-text lists and Dunia `binary_classes.xml` definitions are supported; XML parsing prohibits DTD resolution.

Targeted ASCII scanning of the local `FC_m64.dll` against the eight hashes in entry 93 produced six exact matches:

| Hash | Name |
|---:|---|
| `1EE89A13` | `LibraryId` |
| `1F027DA1` | `NomadObject` |
| `25368426` | `hid_DTCTH_ClassName` |
| `723A4D89` | `SpawnThreadSafe` |
| `7D2D7152` | `LibraryPathName` |
| `EF14ED8F` | `LibraryVersion` |

`CF68E402` and `E7046466` remain unresolved and are rendered as `<unknown:HASH>`. The FC2 definitions resolve none of the eight hashes, so FC2 names are not reused as FC5 labels.

## Next gate

Expand the evidence-backed FC5 dictionary across all discovered FCB resources, then introduce typed value codecs. Keep mutation disabled for unresolved fields instead of guessing their semantics.
