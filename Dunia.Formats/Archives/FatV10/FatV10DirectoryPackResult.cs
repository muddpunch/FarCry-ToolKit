namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10DirectoryPackResult(
    FatV10ArchivePatchFileBuildResult Build,
    IReadOnlyList<FatV10DirectoryPackItem> Items);
