namespace Dunia.Formats.Fcb;

internal sealed record FcbArchiveTransactionArtifact(
    IReadOnlyList<FcbArchiveTransactionEntry> Requests,
    IReadOnlyList<FcbArchiveBatchMutationArtifact> Entries,
    FcbArchiveTransactionPlanResult Plan,
    long SourceDataLength);
