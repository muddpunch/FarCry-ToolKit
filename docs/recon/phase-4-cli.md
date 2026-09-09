# Phase 4: CLI completion

## Directory pack — 2026-09-09

`pack <source.fat> <output.fat> <changed-resource-directory>` recursively maps relative input paths to normalized Dunia CRC64 resource identities. Unknown resources, input hash collisions, source target changes, existing destinations, reparse-point traversal, oversized payloads, and staged SHA-256 mismatches are rejected.

One input replaces every archive entry carrying the same resource hash. The source DAT remains byte-exact, replacements are appended with FAT v10 alignment, and the published pair is reparsed and independently checked against every staged payload before success.

## Resource references — 2026-09-09

`refs <archive.fat> <entry-index> [--names paths.txt]` extracts the selected payload into an automatically deleted temporary stream and scans every overlapping 8-byte window for hashes present in the archive index. Little- and big-endian matches report their exact payload offsets, resource hashes, and optional resolved names.

The scanner preserves seven bytes across 1 MiB buffer boundaries, supports non-seekable inputs, reports absolute offsets for seekable streams, and enforces a 1,000,000-match safety bound.

## Acceptance

- New output archive pairs are never overwritten.
- Pack replacement bytes are re-extracted and SHA-256 verified.
- Unknown pack resources fail without publishing either output file.
- Reference matches crossing a streaming buffer boundary are detected once at the correct offset.
- CLI help contains no advertised placeholder verbs.

The WPF archive browser exposes **Pack changed directory** as a new-pair-only operation and **Find resource references** for the selected entry. Reference results open in a keyboard-focused virtualized table; Escape closes the modal and every row exposes a complete automation name.
