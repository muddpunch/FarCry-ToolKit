# Phase 1: archive layer

**Status: complete.** The shared format library implements validated FAT v10 reading, extraction,
rebuilding, transactional replacement, permanent first-write backups, restore, and rollback.

## Acceptance

- All 16 local FC5 indexes parse with bounded DAT ranges.
- Empty and compressed real archive pairs rebuild byte-for-byte.
- Uncompressed and raw-LZ4 payload extraction reproduces expected SHA-256 values.
- New-pair publication refuses existing destinations and removes partial outputs on failure.
- In-place Apply binds expected identities and source hashes, locks the source pair, creates or verifies
  immutable `.original` backups, and rolls both files back on any publication or semantic failure.
- Restore requires the expected backup hashes and retains immutable backups after success.
- Directory pack rejects unknown resources, collisions, reparse traversal, and stale inputs.

The CLI, WPF application, FCB transactions, and texture transactions all use this shared boundary;
none implements a separate archive writer.
