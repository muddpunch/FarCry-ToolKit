using System.Buffers.Binary;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbArchiveMutationDryRunServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FcbArchiveMutationDryRunServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task RunAsyncMutatesRebuildsVerifiesAndRemovesOutputs()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair source = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(source.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationDryRunResult result = await FcbArchiveMutationDryRunService.RunAsync(
            source,
            0,
            0x0123456789ABCDEF,
            schema,
            0,
            0,
            0x10,
            0x20,
            "false",
            directory,
            token);

        Assert.True(result.IsVerified);
        Assert.Equal(FcbValueKind.Boolean, result.Codec);
        Assert.Equal(2, result.ArchiveEntryCount);
        Assert.Equal(fcb.Length, result.PayloadLength);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(source.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(source.DatPath, token));
        Assert.Empty(Directory.EnumerateDirectories(directory, "fcb-dryrun-*"));
    }

    [Fact]
    public async Task CreateAsyncPublishesVerifiedCopyWithoutChangingSource()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair source = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(source.FatPath, token);
        var destination = new ArchivePair(
            Path.Combine(directory, "mutated.fat"),
            Path.Combine(directory, "mutated.dat"));
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationCopyResult result = await FcbArchiveMutationCopyService.CreateAsync(
            source,
            destination,
            0,
            0x0123456789ABCDEF,
            schema,
            0,
            0,
            0x10,
            0x20,
            "false",
            directory,
            token);

        Assert.True(result.PayloadExact);
        Assert.True(File.Exists(destination.FatPath));
        Assert.True(File.Exists(destination.DatPath));
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(source.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(source.DatPath, token));
        using FileStream fat = File.OpenRead(destination.FatPath);
        FatV10Entry outputEntry = FatV10IndexReader.Read(
            fat,
            new FileInfo(destination.DatPath).Length).Entries[0];
        await using FileStream data = File.OpenRead(destination.DatPath);
        await using var payload = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(data, outputEntry, payload, token);
        payload.Position = 0;
        Assert.Equal<byte>([0], FcbReader.Read(payload).Root.Fields[0].Data.ToArray());
    }

    [Fact]
    public async Task ApplyAsyncBacksUpPublishesAndSemanticallyVerifiesMutation()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair target = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationApplyResult result = await FcbArchiveMutationApplyService.ApplyAsync(
            target,
            0,
            0x0123456789ABCDEF,
            Convert.ToHexString(SHA256.HashData(fcb)),
            schema,
            0,
            0,
            0x10,
            0x20,
            "false",
            directory,
            token);

        Assert.True(result.SemanticVerified);
        Assert.False(result.NoOp);
        Assert.True(Assert.IsType<ArchivePairBackupResult>(result.Backup).CreatedAny);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath + ".original", token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(target.DatPath + ".original", token));
        using FileStream fat = File.OpenRead(target.FatPath);
        FatV10Entry outputEntry = FatV10IndexReader.Read(
            fat,
            new FileInfo(target.DatPath).Length).Entries[0];
        await using FileStream data = File.OpenRead(target.DatPath);
        await using var payload = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(data, outputEntry, payload, token);
        payload.Position = 0;
        Assert.Equal<byte>([0], FcbReader.Read(payload).Root.Fields[0].Data.ToArray());
        Assert.Empty(Directory.EnumerateFiles(directory, "*.rollback-*.tmp"));
    }

    [Fact]
    public async Task ApplyAsyncShortCircuitsNoOpWithoutBackupOrArchiveWrite()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair target = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationApplyResult result = await FcbArchiveMutationApplyService.ApplyAsync(
            target,
            0,
            0x0123456789ABCDEF,
            Convert.ToHexString(SHA256.HashData(fcb)),
            schema,
            0,
            0,
            0x10,
            0x20,
            "1",
            directory,
            token);

        Assert.True(result.NoOp);
        Assert.True(result.SemanticVerified);
        Assert.Null(result.Backup);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
        Assert.Empty(Directory.EnumerateDirectories(directory, "fcb-dryrun-*"));
        Assert.Empty(Directory.EnumerateDirectories(directory, "session-*"));
    }

    [Fact]
    public async Task ApplyAsyncRejectsResourceMismatchBeforeCreatingBackups()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        ArchivePair target = CreateArchive(fcb.Length, fcb);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        await Assert.ThrowsAsync<InvalidDataException>(() => FcbArchiveMutationApplyService.ApplyAsync(
            target,
            0,
            0,
            Convert.ToHexString(SHA256.HashData(fcb)),
            schema,
            0,
            0,
            0x10,
            0x20,
            "false",
            directory,
            token));

        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(fcb, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
    }

    [Fact]
    public async Task ApplyAsyncRejectsStalePlanBeforeBackupOrArchiveWrite()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair target = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FcbArchiveMutationApplyService.ApplyAsync(
                target,
                0,
                0x0123456789ABCDEF,
                new string('0', 64),
                schema,
                0,
                0,
                0x10,
                0x20,
                "false",
                directory,
                token));

        Assert.Contains("Source payload SHA-256 mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
        Assert.Empty(Directory.EnumerateDirectories(directory, "fcb-dryrun-*"));
        Assert.Empty(Directory.EnumerateDirectories(directory, "session-*"));
    }

    [Fact]
    public async Task MutationPlanReportsTypedValuesAndPredictedPayloadWithoutWrites()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair source = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(source.FatPath, token);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationPlanResult result = await FcbArchiveMutationPlanService.CreateAsync(
            source,
            0,
            0x0123456789ABCDEF,
            schema,
            0,
            0,
            0x10,
            0x20,
            "false",
            token);

        Assert.Equal("true", result.CurrentValue);
        Assert.Equal("false", result.RequestedValue);
        Assert.Equal("00", result.RequestedEncodedHex);
        Assert.False(result.NoOp);
        Assert.NotEqual(result.SourcePayloadSha256, result.PlannedPayloadSha256);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(source.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(source.DatPath, token));
        Assert.False(File.Exists(source.FatPath + ".original"));
    }

    [Fact]
    public async Task MutationPlanCanonicalizesEquivalentValueAsNoOp()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFcb(true);
        ArchivePair source = CreateArchive(fcb.Length, [.. fcb, 0xAA, 0xBB]);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationPlanResult result = await FcbArchiveMutationPlanService.CreateAsync(
            source,
            0,
            0x0123456789ABCDEF,
            schema,
            0,
            0,
            0x10,
            0x20,
            "1",
            token);

        Assert.Equal("true", result.RequestedValue);
        Assert.Equal("01", result.RequestedEncodedHex);
        Assert.True(result.NoOp);
        Assert.Equal(result.SourcePayloadSha256, result.PlannedPayloadSha256);
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreateArchive(int fcbLength, byte[] data)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        FatV10Entry[] entries =
        [
            new(0x0123456789ABCDEF, fcbLength, 0, fcbLength, FatV10CompressionScheme.None, false),
            new(0x1111111111111111, 2, fcbLength, 2, FatV10CompressionScheme.None, false),
        ];
        using (FileStream fat = File.Create(fatPath))
        {
            FatV10IndexWriter.Write(fat, entries);
        }

        File.WriteAllBytes(datPath, data);
        return new(fatPath, datPath);
    }

    private static byte[] CreateBooleanFcb(bool value)
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(1);
        WriteUInt32(body, 0x20);
        body.WriteByte(1);
        body.WriteByte(value ? (byte)1 : (byte)0);
        byte[] data = new byte[FcbReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        body.ToArray().CopyTo(data, FcbReader.HeaderSize);
        return data;
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
