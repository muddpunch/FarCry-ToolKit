# XBT archive transaction API v1

`XbtArchiveReplacementService` is the stable single-entry backend boundary used by the CLI.
`XbtArchiveTransactionService` binds the WPF pending set into one deterministic multi-entry Apply.
Consumers must not compose archive rebuilding, backup, publication, or rollback primitives directly.

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

The WPF session stages verified payloads with `ReplacementStagingStore`, then uses this multi-entry
boundary:

```csharp
public static class XbtArchiveTransactionService
{
    public static Task<XbtArchiveTransactionPlan> PlanAsync(
        ArchivePair source,
        IReadOnlyCollection<XbtArchiveTransactionItem> items,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default);

    public static Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        string expectedPlanSha256,
        IReadOnlyCollection<XbtArchiveTransactionItem> items,
        ReplacementStagingStore stagingStore,
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
- Multi-entry plans canonicalize replacements by entry index and reject duplicate indexes.
- Multi-entry Apply re-plans under the source lock and validates every published XBT before rollback removal.
