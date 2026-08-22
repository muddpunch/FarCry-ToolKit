namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchivePatchBuildResult(
    FatV10Index Index,
    long DataLength,
    int ReplacementCount);
