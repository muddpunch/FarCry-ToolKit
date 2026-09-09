# XBT archive transaction API v1

`XbtArchiveReplacementService` is the stable backend boundary for CLI and WPF texture replacement. Consumers must not compose archive staging, rebuilding, backup, publication, or rollback primitives directly.

## Contract

```csharp
public static class XbtArchiveReplacementService
{
    public static Task<XbtArchiveReplacementPlan> PlanAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        CancellationToken cancellationToken = default);

    public static Task<XbtArchiveReplacementDryRunResult> DryRunAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string temporaryRoot,
        CancellationToken cancellationToken = default);

    public static Task<FatV10ArchivePatchFileBuildResult> CopyAsync(
        ArchivePair source,
        ArchivePair destination,
        string expectedPlanSha256,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string stagingRoot,
        CancellationToken cancellationToken = default);

    public static Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        string expectedPlanSha256,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string stagingRoot,
        CancellationToken cancellationToken = default);
}
```

## Invariants

- All input XBT payloads must decode successfully before a plan is returned.
- The plan SHA-256 binds entry index, resource hash, source payload SHA-256, replacement payload SHA-256, and replacement length.
- Dry-run and Copy revalidate the bound source payload after opening the source pair for rebuilding.
- Copy never overwrites an existing destination pair and removes both new files if semantic validation fails.
- Apply requires the exact plan SHA-256, fingerprints and locks both source files, and revalidates the source payload before rebuilding.
- Apply creates or retains immutable `.original` backups before publication.
- Published replacement bytes must match the staged SHA-256 and length, then decode as XBT before rollback files are removed.
- Any publication or semantic validation failure restores both pre-transaction archive files.
