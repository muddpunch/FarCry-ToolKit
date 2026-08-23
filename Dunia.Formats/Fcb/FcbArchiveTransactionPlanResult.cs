namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveTransactionPlanResult(
    int ApiVersion,
    int ArchiveEntryCount,
    IReadOnlyList<FcbArchiveBatchMutationPlanResult> Entries,
    string PlanSha256,
    bool NoOp);
