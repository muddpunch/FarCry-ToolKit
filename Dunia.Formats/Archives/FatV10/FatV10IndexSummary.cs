namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10IndexSummary(
    int Platform,
    int EntryCount,
    int EntrySize,
    long IndexLength);

