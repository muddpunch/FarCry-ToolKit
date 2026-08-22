using System.Buffers.Binary;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10PublishedReplacementValidatorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10PublishedReplacementValidatorTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task ValidateAcceptsExactPublishedPayload()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] payload = [1, 2, 3];
        ArchivePair pair = CreatePair(payload);
        var replacement = new StagedReplacement(
            Guid.NewGuid(),
            "replacement.bin",
            payload.Length,
            Convert.ToHexString(SHA256.HashData(payload)));

        await FatV10PublishedReplacementValidator.ValidateAsync(
            pair,
            new Dictionary<int, StagedReplacement> { [0] = replacement },
            token);
    }

    [Fact]
    public async Task ValidateRejectsPublishedPayloadHashMismatch()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([1, 2, 3]);
        var replacement = new StagedReplacement(
            Guid.NewGuid(),
            "replacement.bin",
            3,
            Convert.ToHexString(SHA256.HashData([9, 8, 7])));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FatV10PublishedReplacementValidator.ValidateAsync(
                pair,
                new Dictionary<int, StagedReplacement> { [0] = replacement },
                token));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(byte[] payload)
    {
        var pair = new ArchivePair(
            Path.Combine(directory, "published.fat"),
            Path.Combine(directory, "published.dat"));
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), 1);
        FatV10EntryWriter.Write(
            new(1, payload.Length, 0, payload.Length, FatV10CompressionScheme.None, false),
            fat.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        File.WriteAllBytes(pair.FatPath, fat);
        File.WriteAllBytes(pair.DatPath, payload);
        return pair;
    }
}
