namespace Dunia.Formats.Archives;

public static class ArchivePairBackupService
{
    public static async Task<ArchivePairBackupResult> EnsureCreatedAsync(
        ArchivePair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDistinctPaths(pair);
        EnsureSourceExists(pair.FatPath);
        EnsureSourceExists(pair.DatPath);

        ArchiveBackupResult fat = await ArchiveBackupService.EnsureCreatedAsync(
            pair.FatPath,
            cancellationToken).ConfigureAwait(false);
        ArchiveBackupResult dat = await ArchiveBackupService.EnsureCreatedAsync(
            pair.DatPath,
            cancellationToken).ConfigureAwait(false);

        return new(fat, dat);
    }

    private static void ValidateDistinctPaths(ArchivePair pair)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(pair.FatPath, pair.DatPath, comparison))
        {
            throw new ArgumentException("FAT and DAT paths must be different.", nameof(pair));
        }
    }

    private static void EnsureSourceExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Archive source file was not found.", path);
        }
    }
}

