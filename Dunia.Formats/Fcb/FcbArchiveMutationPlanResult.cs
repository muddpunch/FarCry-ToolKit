namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMutationPlanResult(
    int EntryIndex,
    ulong ResourceNameHash,
    int NodeIndex,
    int FieldIndex,
    uint TypeHash,
    uint FieldHash,
    FcbValueKind Codec,
    string CurrentValue,
    string RequestedValue,
    string RequestedEncodedHex,
    int SchemaFieldCount,
    int SchemaResolvedCount,
    int SourcePayloadLength,
    string SourcePayloadSha256,
    int PlannedPayloadLength,
    string PlannedPayloadSha256,
    bool NoOp);
