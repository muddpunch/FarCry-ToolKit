using System.Buffers;
using System.Security.Cryptography;

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
            return new(backupPath, false, await ComputeSha256Async(backupPath, cancellationToken).ConfigureAwait(false));
        }

        string temporaryPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            string sourceSha256 = await CopyToTemporaryFileAsync(sourcePath, temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            string backupSha256 = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceSha256, backupSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archive backup failed SHA-256 verification.");
            }

            try
            {
                File.Move(temporaryPath, backupPath, false);
                return new(backupPath, true, backupSha256);
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                return new(
                    backupPath,
                    false,
                    await ComputeSha256Async(backupPath, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<string> CopyToTemporaryFileAsync(
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
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer.AsSpan(0, read));
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, true);
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        target.Flush(true);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
