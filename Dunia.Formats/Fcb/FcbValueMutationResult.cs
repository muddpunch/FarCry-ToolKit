namespace Dunia.Formats.Fcb;

public sealed record FcbValueMutationResult(
    FcbDocument Document,
    ReadOnlyMemory<byte> Data,
    FcbValueKind Codec);
