namespace Dunia.Formats.Fcb;

public sealed record FcbFieldReplacement(
    FcbField Target,
    ReadOnlyMemory<byte> Data);
