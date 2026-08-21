namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10Index(
    FatV10IndexSummary Summary,
    IReadOnlyList<FatV10Entry> Entries);

