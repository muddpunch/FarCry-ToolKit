namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMutationDryRunResult(
    int EntryIndex,
    ulong ResourceNameHash,
    FcbValueKind Codec,
    int PayloadLength,
    string PayloadSha256,
    int ArchiveEntryCount,
    bool PayloadExact,
    bool UntouchedEntriesExact,
    bool SourceDataPrefixExact)
{
    public bool IsVerified => PayloadExact && UntouchedEntriesExact && SourceDataPrefixExact;
}
