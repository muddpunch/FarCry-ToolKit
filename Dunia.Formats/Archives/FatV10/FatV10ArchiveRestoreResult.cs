namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchiveRestoreResult(
    ArchivePair TargetPair,
    ArchivePairBackupResult Backup,
    int ArchiveEntryCount,
    bool Verified);
