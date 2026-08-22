namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchivePatchApplyResult(
    ArchivePairBackupResult Backup,
    FatV10ArchivePatchBuildResult Build);
