namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveTransactionDryRunResult(
    FcbArchiveTransactionPlanResult Plan,
    int ReplacementEntryCount,
    bool PayloadsExact,
    bool UntouchedEntriesExact,
    bool SourceDataPrefixExact)
{
    public bool IsVerified => PayloadsExact && UntouchedEntriesExact && SourceDataPrefixExact;
}
