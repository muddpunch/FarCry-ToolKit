namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveBatchMutationDryRunResult(
    FcbArchiveBatchMutationPlanResult Plan,
    bool PayloadExact,
    bool UntouchedEntriesExact,
    bool SourceDataPrefixExact)
{
    public bool IsVerified => PayloadExact && UntouchedEntriesExact && SourceDataPrefixExact;
}
