namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10Entry(
    ulong NameHash,
    int UncompressedSize,
    long Offset,
    int StoredSize,
    FatV10CompressionScheme CompressionScheme,
    bool IsEncrypted);

