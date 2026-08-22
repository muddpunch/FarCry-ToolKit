using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveResource(
    int EntryIndex,
    ulong NameHash,
    int UncompressedSize,
    FatV10CompressionScheme CompressionScheme,
    int UniqueNodeCount,
    int FieldCount);
