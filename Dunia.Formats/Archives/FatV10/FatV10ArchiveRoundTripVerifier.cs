using System.Security.Cryptography;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchiveRoundTripVerifier
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FatV10ArchiveRoundTripVerificationResult> VerifyAsync(
        ArchivePair source,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(temporaryRoot);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, $"roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionPath);

        try
        {
            var output = new ArchivePair(
                Path.Combine(sessionPath, "roundtrip.fat"),
                Path.Combine(sessionPath, "roundtrip.dat"));
            using (var store = new ReplacementStagingStore(sessionPath))
            {
                await FatV10ArchivePatchFileBuilder.BuildAsync(
                    source,
                    output,
                    new Dictionary<int, StagedReplacement>(),
                    store,
                    cancellationToken).ConfigureAwait(false);
            }

            string sourceFatHash = await ComputeSha256Async(source.FatPath, cancellationToken).ConfigureAwait(false);
            string outputFatHash = await ComputeSha256Async(output.FatPath, cancellationToken).ConfigureAwait(false);
            string sourceDatHash = await ComputeSha256Async(source.DatPath, cancellationToken).ConfigureAwait(false);
            string outputDatHash = await ComputeSha256Async(output.DatPath, cancellationToken).ConfigureAwait(false);
            return new(
                sourceFatHash,
                outputFatHash,
                sourceDatHash,
                outputDatHash,
                new FileInfo(source.FatPath).Length,
                new FileInfo(source.DatPath).Length);
        }
        finally
        {
            if (Directory.Exists(sessionPath))
            {
                Directory.Delete(sessionPath, true);
            }
        }
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
