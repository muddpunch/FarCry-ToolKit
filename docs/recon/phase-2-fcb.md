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

## Archive-wide validation — 2026-08-22

`common.fat` was analyzed read-only in one pass. Every signature-positive payload parsed successfully:

| Property | Value |
|---|---:|
| FAT entries | 3,401 |
| Eligible/scanned entries | 3,366 |
| Size-filtered entries | 35 |
| Parsed FCB resources | 343 |
| Unique type hashes | 4,402 |
| Type occurrences | 197,875 |
| Unique field hashes | 8,660 |
| Field occurrences | 1,026,097 |

Targeted ASCII discovery against the local `FC_m64.dll` checked 13,345,198 printable candidates against 12,932 distinct FCB hashes and emitted 11,611 exact CRC32 candidate mappings. Re-auditing with the generated map produced:

| Category | Resolved occurrences | Unknown occurrences | Collision occurrences | Unknown unique hashes |
|---|---:|---:|---:|---:|
| Types | 194,498 | 3,316 | 61 | 713 |
| Fields | 925,153 | 100,876 | 68 | 612 |

Generated maps use verified `HASH<TAB>NAME` records, are atomically published without overwriting existing files, and retain every collision candidate. The local generated map is not committed.

## Read-only value projections — 2026-08-22

`FcbValueProjector` always retains raw hex and emits zero or more explicitly qualified candidates:

- `Structural`: currently limited to complete printable ASCII strings terminated by NUL.
- `SizeCompatible`: boolean, signed/unsigned integers, IEEE-754 values, and 2/3/4-component binary32 vectors based only on byte length.

No size-compatible candidate is selected automatically. Real entry 93 exposes `SNomadDbLibLoader` and `Retargeting_ObjectSettings` as structural strings while its 1/4/8-byte fields remain visibly ambiguous. Running the projection does not modify the graph; the fixture still passes byte-exact SHA-256 round-trip verification.

## External value schema and coverage gate — 2026-08-22

`FcbValueSchema` loads exact `(node type hash, field hash) -> codec` mappings. Duplicate keys, malformed hashes, and unknown codecs are rejected. `FcbTypedValueProjector` reports `Resolved`, `MissingSchema`, or `Incompatible` without modifying raw bytes.

`FcbValueSchemaCoverageAnalyzer` traverses the graph and requires every field to resolve against a compatible schema codec. Real entry 93 passes with `fields=6`, `resolved=6`, `missing=0`, `incompatible=0`, and `complete=true` using `data/fcb-schema.fc5.txt`.

## Schema-gated mutation API — 2026-08-22

`FcbValueMutator.ReplaceInlineField` requires complete source schema coverage, verifies replacement compatibility with the exact pair codec, clones graph identity, propagates inline-value changes through backward references, and accepts the result only after serialize/reparse stability and a second complete coverage audit.

Referenced fields cannot be replaced independently.

## Atomic mutation CLI — 2026-08-22

`fcb mutate` requires node and field indexes plus expected type and field hashes. Values are encoded by the schema-selected codec, input and existing outputs cannot be overwritten, and the verified result is published through a same-directory temporary file followed by an atomic move.

Real entry 93 was copied and `E7046466:723A4D89` (`SpawnThreadSafe`) was changed to `true`. The output retained complete 6/6 schema coverage and passed byte-exact read/write verification with SHA-256 `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`. A repeated command targeting the same output was rejected without overwrite.

## FAT/DAT mutation dry-run — 2026-08-22

`fcb archive-mutate-dry-run` extracts the selected payload in memory, verifies the expected 64-bit resource hash and exact FCB field identity, performs the schema-gated mutation, stages the result from memory, rebuilds a temporary archive pair, and deletes the pair after validation.

Real `common.fat` entry 93 passed with `payload.exact=true`, `entries.untouched=true`, `dat.prefix.exact=true`, and `source.modified=false`. The rebuilt payload SHA-256 was `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`.

## Verified archive-copy publication — 2026-08-22

`fcb archive-mutate-copy` requires the complete dry-run to succeed, regenerates the mutation and requires the same payload SHA-256, publishes only to a nonexistent FAT/DAT destination, then independently extracts and compares the stored payload. Source files are opened read-only.

A real copy built from `common.fat` entry 93 passed independent extraction, complete 6/6 schema coverage, and byte-exact FCB verification. `SpawnThreadSafe` resolved to `true`; payload SHA-256 remained `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`.

## Manual game-load validation — 2026-08-23

The verified `common.fat/common.dat` copy containing the entry 93 mutation completed the user-run Far Cry 5 load test without a reported archive error or crash. The test harness restored both original files afterward. The published payload remained 120 bytes with SHA-256 `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`.

## Confirmed in-place FCB apply — 2026-08-23

`fcb archive-mutate-apply` is gated by the literal `--confirm-write`, expected resource/type/field hashes, the planned source payload SHA-256, complete schema coverage, and a successful archive dry-run. It creates verified `.original` backups before publication. While rollback files still exist, it re-extracts and SHA-256-validates the staged replacement, requires exact mutated payload bytes, reparses the FCB, verifies the encoded target value and full schema coverage, and requires byte-exact FCB serialization. Any mismatch, exception, or cancellation restores both originals.

## Confirmed in-place game validation — 2026-08-23

The confirmed command applied the entry 93 mutation directly to the local `common.fat/common.dat` pair with `semantic.verified=true`. The mutated 120-byte payload had SHA-256 `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`; independent extraction resolved `SpawnThreadSafe=true`. Far Cry 5 completed the user-run test with no crash. The original pair was then restored and independently matched the immutable backups: FAT SHA-256 `7BFF1EF1FA6175F36DBC2546A1FFE5B499DEC70845879472686AE359E72DDDD8`, DAT SHA-256 `228CB95FD5918CC62E56BDB117912E8CCB9115A55845528CCA8C56690B0241D7`.

## Confirmed archive restore — 2026-08-23

`restore <archive.fat> --confirm-write <fat-sha256> <dat-sha256>` requires the expected hashes of both immutable `.original` files. It copies and durably flushes both backups to temporary files, verifies their hashes and FAT/DAT structure, publishes the pair while retaining rollback files, and rechecks the live hashes before deleting the rollback pair. Failure or cancellation restores both pre-command files; `.original` backups are never moved or overwritten.

The command completed successfully against the local `common.fat/common.dat` backup pair with `entries=3401` and `verified=true`. Independent post-command checks found byte-identical live and `.original` hashes, valid FAT layout and DAT bounds, and no remaining temporary files.

## Read-only mutation planning — 2026-08-23

`fcb mutation-plan` validates the resource/type/field identities and complete schema coverage, projects the current and canonical requested values, encodes the request, performs the schema-gated mutation entirely in memory, and reports source/planned payload lengths and SHA-256 hashes. It performs no staging, backup, archive rebuild, or write.

Against real `common.fat` entry 93, planning `false -> true` predicted the independently validated payload SHA-256 `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D` with `no-op=false`. Planning `false -> false` retained source SHA-256 `B499881AD3C7E7DA3DD846CBEAABAF7C7EAD094573196B3FB4285B8EE7378CAD` with `no-op=true`.

## Semantic no-op apply — 2026-08-23

Confirmed FCB apply now executes the read-only plan first. A semantic no-op returns success with `applied=false`, `no-op=true`, `semantic.verified=true`, and `backup.created=false` before dry-run, staging, backup creation, or archive rebuilding.

The behavior passed against real `common.fat` entry 93 for `false -> false`. Independent before/after checks confirmed identical FAT/DAT lengths, last-write timestamps, and SHA-256 values.

## Plan-bound confirmed apply — 2026-08-23

Confirmed FCB apply now requires the exact `payload.source.sha256` emitted by `mutation-plan`. A mismatch is rejected before no-op handling, dry-run, staging, backup creation, or archive rebuilding. The result reports both source and planned/published payload hashes.

A real entry 93 invocation with a stale all-zero hash returned exit code `1` and left FAT/DAT unchanged. The matching source hash retained the verified no-op path. Expected validation errors are now mapped consistently to concise CLI messages without unhandled stack traces.

## Source-pair stability gate — 2026-08-23

Transactional apply now opens both source files with read sharing only, fingerprints their lengths and SHA-256 values, and holds both handles through archive construction and permanent-backup creation. After releasing the handles, publication first moves the live pair to rollback paths and requires the moved pair to match the original fingerprint before installing either rebuilt file.

A synthetic race regression changes the source DAT after construction and before publication. Apply rejects the stale build, restores the changed live pair byte-for-byte, leaves the rebuilt pair unpublished, and removes both rollback files.

## Atomic multi-field graph mutation — 2026-08-23

`FcbValueMutator.ReplaceInlineFields` validates every target and encoded value before cloning, applies all replacements to one graph clone, propagates changes through backward references, then performs one serialize/reparse/schema-coverage verification cycle. Duplicate targets and referenced-field targets are rejected before an output document is returned; the existing single-field API delegates to the same transaction.

## Atomic multi-field archive mutation — 2026-08-23

`mutation-plan-batch`, `archive-mutate-batch-dry-run`, `archive-mutate-batch-copy`, and `archive-mutate-batch-apply` consume tab-separated node/field identity records. The planner extracts and parses the selected FCB once, validates every identity and codec, applies all requested values to one clone, and emits one planned payload hash. Dry-run rebuilds one temporary archive pair and verifies the payload, untouched FAT entries, and original DAT prefix.

Batch copy publishes only to a nonexistent pair and repeats the plan while both source archive files are read-locked. Confirmed batch apply additionally requires the planned source payload SHA-256, uses the source-pair fingerprinted transaction, validates every published field plus complete schema coverage and byte-exact FCB round-trip, and retains rollback files until all semantic checks pass. A two-field synthetic archive passes the complete plan/dry-run/copy/backup/apply path.

## Mutation template generation — 2026-08-23

`mutation-template` extracts and schema-audits one archive FCB, projects every independently editable inline field, verifies that each canonical value re-encodes byte-exactly, and atomically writes a UTF-8 TSV without overwriting existing output. Each record contains node/field indexes, expected type/field hashes, and an escaped canonical value; the header includes the source payload SHA-256 required by confirmed batch apply.

Loading the unchanged generated template must produce a byte-exact semantic no-op. Referenced fields are reported and excluded because they cannot be independently replaced.

## Real mutation-template and batch-copy validation — 2026-08-23

Generating a template from real `common.fat` entry 93 produced six editable records, zero skipped references, complete 6/6 schema coverage, and source payload SHA-256 `B499881AD3C7E7DA3DD846CBEAABAF7C7EAD094573196B3FB4285B8EE7378CAD`. Loading the unchanged template planned a byte-exact 120-byte no-op.

Changing only the generated `SpawnThreadSafe` record to `true` produced planned payload SHA-256 `78F9322A533671022DEDF2B8BCF6F8BB8D911C6D9DC59C9C945A60E3DE20226D`. Batch dry-run passed payload, untouched-entry, and DAT-prefix verification. Batch copy produced FAT SHA-256 `EB57C1D0E3DAED85B3AE88E9CEA83B760AF0F18F9266174B83FD6C5F49672E8A` and DAT SHA-256 `A3CF0A8195B6AC43D93A78D8B7509BDCA22D3A9D9289D110885CC2BE9536B9D0`, byte-identical to the previously game-load-tested archive pair. The live game FAT/DAT lengths and timestamps remained unchanged.

## Multi-entry archive transaction and API freeze — 2026-08-23

`FcbArchiveTransactionService` API v1 generalizes mutation to multiple FCB entries. It canonicalizes entry and field order, produces one deterministic plan SHA-256 binding every source and planned payload, stages only changed entries, performs one dry-run rebuild, and publishes one copied or in-place archive transaction. Copy and Apply repeat the entire plan under the source FAT/DAT read lock. Confirmed Apply rejects a stale plan before backup or writes and validates every targeted payload, field, schema, and FCB round-trip before removing rollback files.

A synthetic two-entry archive passes Plan, order-independent fixed plan hashing, one-rebuild Dry-run, Copy, permanent backup, transactional Apply, and semantic verification of both published entries. A reflection contract test freezes API version 1 and the exact four public facade signatures. The WPF layer must consume this facade rather than lower-level archive-writing services; the authoritative contract is [`../api/fcb-archive-transaction-v1.md`](../api/fcb-archive-transaction-v1.md).

## WPF transaction workspace — 2026-09-06

The archive browser now opens one or more schema-compatible `.fcb` entries in a dedicated transaction window. Extended row selection feeds a single pending workspace, editable fields retain their archive entry identity, and mutations are grouped deterministically into the API v1 multi-entry request. Editable inline fields are projected through the external schema, pending values remain in memory, and **Discard** restores the loaded manifests without touching the archive. **Plan**, **Dry-run**, **Create copy**, and **Apply** call the frozen `FcbArchiveTransactionService` API v1 facade; WPF does not compose lower-level staging, builder, backup, or rollback services. Apply is bound to the exact displayed plan SHA-256 and requires an explicit warning confirmation before the facade creates or reuses immutable backups.

## Next gate

Validate the WPF workspace interactively against real `common.fat` entry 93 and a multi-entry selection.
