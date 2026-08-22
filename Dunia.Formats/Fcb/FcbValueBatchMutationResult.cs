namespace Dunia.Formats.Fcb;

public sealed record FcbValueBatchMutationResult(
    FcbDocument Document,
    ReadOnlyMemory<byte> Data,
    IReadOnlyList<FcbValueKind> Codecs);
