namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveBatchMutationPlanResult(
    int EntryIndex,
    ulong ResourceNameHash,
    int ArchiveEntryCount,
    IReadOnlyList<FcbArchivePlannedFieldMutation> Mutations,
    int SchemaFieldCount,
    int SchemaResolvedCount,
    int SourcePayloadLength,
    string SourcePayloadSha256,
    int PlannedPayloadLength,
    string PlannedPayloadSha256,
    bool NoOp);
