using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMatch(
    int EntryIndex,
    ulong NameHash,
    int UncompressedSize,
    FatV10CompressionScheme CompressionScheme);
