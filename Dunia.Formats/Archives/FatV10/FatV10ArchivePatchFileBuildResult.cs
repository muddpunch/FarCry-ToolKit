namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchivePatchFileBuildResult(
    ArchivePair OutputPair,
    FatV10ArchivePatchBuildResult Build);
