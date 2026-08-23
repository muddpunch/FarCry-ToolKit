# FCB archive transaction API v1

`FcbArchiveTransactionService` is the stable backend boundary for CLI and WPF archive mutation workflows. UI code must depend on this facade instead of composing lower-level mutation, staging, builder, backup, or rollback services.

## Contract

```csharp
public static class FcbArchiveTransactionService
{
    public const int ApiVersion = 1;

    public static Task<FcbArchiveTransactionPlanResult> PlanAsync(
        ArchivePair source,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        CancellationToken cancellationToken = default);

    public static Task<FcbArchiveTransactionDryRunResult> DryRunAsync(
        ArchivePair source,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string temporaryRoot,
        CancellationToken cancellationToken = default);

    public static Task<FcbArchiveTransactionCopyResult> CreateCopyAsync(
        ArchivePair source,
        ArchivePair destination,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string temporaryRoot,
        CancellationToken cancellationToken = default);

    public static Task<FcbArchiveTransactionApplyResult> ApplyAsync(
        ArchivePair target,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string expectedPlanSha256,
        string temporaryRoot,
        CancellationToken cancellationToken = default);
}
```

## Versioning

- Additive result properties do not require a version increment.
- Parameter reordering, removal, semantic changes, or plan-hash canonicalization changes require API v2.
- `ApplyAsync` must remain gated by the exact SHA-256 returned from `PlanAsync`.
- No-op Apply must return before staging, backup, rebuild, or archive writes.
- Copy and Apply must re-plan targeted entries while the source FAT/DAT pair is read-locked.
- Apply must retain rollback files until payload, schema, field, and FCB round-trip validation succeeds for every targeted entry.

The contract test asserts the API version, exact operation set, signatures, order-independent plan hash, and a fixed canonical plan-hash fixture.
