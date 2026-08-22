using System.Buffers;
using System.Security.Cryptography;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchiveRestoreService
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FatV10ArchiveRestoreResult> RestoreAsync(
        ArchivePair target,
        string expectedFatSha256,
        string expectedDatSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        expectedFatSha256 = NormalizeSha256(expectedFatSha256, nameof(expectedFatSha256));
        expectedDatSha256 = NormalizeSha256(expectedDatSha256, nameof(expectedDatSha256));
        cancellationToken.ThrowIfCancellationRequested();

        string backupFatPath = target.FatPath + ".original";
        string backupDatPath = target.DatPath + ".original";
        EnsureFileExists(target.FatPath);
        EnsureFileExists(target.DatPath);
        EnsureFileExists(backupFatPath);
        EnsureFileExists(backupDatPath);

        string token = Guid.NewGuid().ToString("N");
        var restored = new ArchivePair(
            $"{target.FatPath}.restore-{token}.tmp",
            $"{target.DatPath}.restore-{token}.tmp");
        string rollbackFatPath = $"{target.FatPath}.rollback-{token}.tmp";
        string rollbackDatPath = $"{target.DatPath}.rollback-{token}.tmp";
        bool published = false;

        try
        {
            string fatSha256 = await CopyAndHashAsync(
                backupFatPath,
                restored.FatPath,
                cancellationToken).ConfigureAwait(false);
            string datSha256 = await CopyAndHashAsync(
                backupDatPath,
                restored.DatPath,
                cancellationToken).ConfigureAwait(false);
            RequireHash(fatSha256, expectedFatSha256, "FAT backup");
            RequireHash(datSha256, expectedDatSha256, "DAT backup");
            FatV10Index restoredIndex = ValidatePair(restored);
            cancellationToken.ThrowIfCancellationRequested();

            await PublishWithRollbackAsync(
                target,
                restored,
                rollbackFatPath,
                rollbackDatPath,
                expectedFatSha256,
                expectedDatSha256,
                cancellationToken).ConfigureAwait(false);
            published = true;

            return new(
                target,
                new(
                    new(backupFatPath, false, fatSha256),
                    new(backupDatPath, false, datSha256)),
                restoredIndex.Entries.Count,
                true);
        }
        finally
        {
            File.Delete(restored.FatPath);
            File.Delete(restored.DatPath);
            if (published)
            {
                File.Delete(rollbackFatPath);
                File.Delete(rollbackDatPath);
            }
        }
    }

    internal static async Task PublishWithRollbackAsync(
        ArchivePair target,
        ArchivePair restored,
        string rollbackFatPath,
        string rollbackDatPath,
        string expectedFatSha256,
        string expectedDatSha256,
        CancellationToken cancellationToken)
    {
        bool originalFatMoved = false;
        bool originalDatMoved = false;
        bool restoredDatMoved = false;
        bool restoredFatMoved = false;

        try
        {
            File.Move(target.FatPath, rollbackFatPath, false);
            originalFatMoved = true;
            File.Move(target.DatPath, rollbackDatPath, false);
            originalDatMoved = true;
            File.Move(restored.DatPath, target.DatPath, false);
            restoredDatMoved = true;
            File.Move(restored.FatPath, target.FatPath, false);
            restoredFatMoved = true;

            ValidatePair(target);
            RequireHash(
                await ComputeSha256Async(target.FatPath, cancellationToken).ConfigureAwait(false),
                expectedFatSha256,
                "restored FAT");
            RequireHash(
                await ComputeSha256Async(target.DatPath, cancellationToken).ConfigureAwait(false),
                expectedDatSha256,
                "restored DAT");
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception publicationError)
        {
            try
            {
                if (restoredFatMoved)
                {
                    File.Delete(target.FatPath);
                }

                if (restoredDatMoved)
                {
                    File.Delete(target.DatPath);
                }

                if (originalDatMoved)
                {
                    File.Move(rollbackDatPath, target.DatPath, false);
                }

                if (originalFatMoved)
                {
                    File.Move(rollbackFatPath, target.FatPath, false);
                }
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Archive restoration failed and rollback could not be completed.",
                    publicationError,
                    rollbackError);
            }

            throw;
        }
    }

    private static FatV10Index ValidatePair(ArchivePair pair)
    {
        long dataLength = new FileInfo(pair.DatPath).Length;
        using FileStream fat = File.OpenRead(pair.FatPath);
        return FatV10IndexReader.Read(fat, dataLength);
    }

    private static async Task<string> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using FileStream source = OpenRead(sourcePath);
            await using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer.AsSpan(0, read));
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(true);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, true);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static string NormalizeSha256(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length != SHA256.HashSizeInBytes * 2 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal digits.", paramName);
        }

        return value.ToUpperInvariant();
    }

    private static void RequireHash(string actual, string expected, string description)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{description} SHA-256 mismatch.");
        }
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required archive file was not found.", path);
        }
    }
}
