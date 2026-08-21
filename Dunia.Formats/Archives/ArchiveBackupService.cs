namespace Dunia.Formats.Archives;

public static class ArchiveBackupService
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<ArchiveBackupResult> EnsureCreatedAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath = Path.GetFullPath(archivePath);
        string backupPath = sourcePath + ".original";

        if (File.Exists(backupPath))
        {
            return new(backupPath, false);
        }

        string temporaryPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await CopyToTemporaryFileAsync(sourcePath, temporaryPath, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                File.Move(temporaryPath, backupPath, false);
                return new(backupPath, true);
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                return new(backupPath, false);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task CopyToTemporaryFileAsync(
        string sourcePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        const FileOptions options = FileOptions.Asynchronous | FileOptions.SequentialScan;

        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            options);
        await using FileStream target = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            options);

        await source.CopyToAsync(target, BufferSize, cancellationToken).ConfigureAwait(false);
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        target.Flush(true);
    }
}

