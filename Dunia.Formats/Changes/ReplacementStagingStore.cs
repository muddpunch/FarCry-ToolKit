using System.Buffers;
using System.Security.Cryptography;

namespace Dunia.Formats.Changes;

public sealed class ReplacementStagingStore : IDisposable
{
    private const int BufferSize = 1024 * 1024;
    private readonly Dictionary<Guid, string> files = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object syncRoot = new();
    private int activeOperations;
    private bool disposing;
    private bool disposed;

    public ReplacementStagingStore(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string fullBaseDirectory = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(fullBaseDirectory);
        SessionPath = Path.Combine(fullBaseDirectory, $"session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(SessionPath);
    }

    internal string SessionPath { get; }

    public async Task<StagedReplacement> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        BeginOperation();

        string? temporaryPath = null;
        string? stagedPath = null;
        byte[]? buffer = null;

        try
        {
            Guid id = Guid.NewGuid();
            string fullSourcePath = Path.GetFullPath(sourcePath);
            temporaryPath = Path.Combine(SessionPath, $"{id:N}.tmp");
            stagedPath = Path.Combine(SessionPath, $"{id:N}.bin");
            buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            CancellationToken token = linkedCancellation.Token;
            token.ThrowIfCancellationRequested();

            (long length, string sha256) = await CopyAndHashAsync(
                fullSourcePath,
                temporaryPath,
                buffer,
                token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, stagedPath, false);
            token.ThrowIfCancellationRequested();

            lock (syncRoot)
            {
                files.Add(id, stagedPath);
            }

            return new(id, Path.GetFileName(fullSourcePath), length, sha256);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(stagedPath);
            throw;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }

            EndOperation();
        }
    }

    public async Task CopyVerifiedToAsync(
        StagedReplacement replacement,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        BeginOperation();
        byte[]? buffer = null;

        try
        {
            buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            string stagedPath = GetTrackedPath(replacement);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            CancellationToken token = linkedCancellation.Token;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;

            await using FileStream source = OpenRead(stagedPath);
            while (true)
            {
                int read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer.AsSpan(0, read));
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                length = checked(length + read);
            }

            string sha256 = Convert.ToHexString(hash.GetHashAndReset());
            if (length != replacement.Length ||
                !string.Equals(sha256, replacement.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Staged replacement failed integrity verification.");
            }
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }

            EndOperation();
        }
    }

    public bool Remove(StagedReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        BeginOperation();

        try
        {
            string stagedPath = GetTrackedPath(replacement);
            File.Delete(stagedPath);

            lock (syncRoot)
            {
                return files.Remove(replacement.Id);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                while (!disposed)
                {
                    Monitor.Wait(syncRoot);
                }

                return;
            }

            disposing = true;
        }

        lifetimeCancellation.Cancel();

        try
        {
            lock (syncRoot)
            {
                while (activeOperations > 0)
                {
                    Monitor.Wait(syncRoot);
                }
            }

            if (Directory.Exists(SessionPath))
            {
                Directory.Delete(SessionPath, true);
            }
        }
        finally
        {
            lifetimeCancellation.Dispose();

            lock (syncRoot)
            {
                files.Clear();
                disposed = true;
                Monitor.PulseAll(syncRoot);
            }
        }
    }

    private static async Task<(long Length, string Sha256)> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        const FileOptions options = FileOptions.Asynchronous | FileOptions.SequentialScan;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;

        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            options);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            options);

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer.AsSpan(0, read));
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            length = checked(length + read);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(true);
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetTrackedPath(StagedReplacement replacement)
    {
        lock (syncRoot)
        {
            if (!files.TryGetValue(replacement.Id, out string? path))
            {
                throw new ArgumentException("Replacement does not belong to this staging store.", nameof(replacement));
            }

            return path;
        }
    }

    private void BeginOperation()
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposing || disposed, this);
            activeOperations++;
        }
    }

    private void EndOperation()
    {
        lock (syncRoot)
        {
            activeOperations--;
            Monitor.PulseAll(syncRoot);
        }
    }
}
