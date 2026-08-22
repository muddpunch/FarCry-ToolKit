namespace Dunia.Formats.Fcb;

public sealed record FcbArchivePlannedFieldMutation(
    int NodeIndex,
    int FieldIndex,
    uint TypeHash,
    uint FieldHash,
    FcbValueKind Codec,
    string CurrentValue,
    string RequestedValue,
    string RequestedEncodedHex);
