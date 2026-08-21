namespace Dunia.Formats.Archives;

public sealed record ArchivePairBackupResult(
    ArchiveBackupResult Fat,
    ArchiveBackupResult Dat)
{
    public bool CreatedAny => Fat.Created || Dat.Created;
}

