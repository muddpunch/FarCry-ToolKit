namespace Dunia.Formats.Fcb;

public sealed record FcbTypedValueProjection(
    FcbTypedValueStatus Status,
    FcbValueKind? Codec,
    string? Value);
