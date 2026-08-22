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

    [Fact]
    public async Task BatchApplyPlansDryRunsAndPublishesAllFieldsAtomically()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFieldsFcb(true, false);
        byte[] originalData = [.. fcb, 0xAA, 0xBB];
        ArchivePair target = CreateArchive(fcb.Length, originalData);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        using var schemaInput = new StringReader(
            "00000010 00000020 Boolean\n00000010 00000021 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);
        FcbArchiveFieldMutation[] mutations =
        [
            new(0, 0, 0x10, 0x20, "false"),
            new(0, 1, 0x10, 0x21, "true"),
        ];

        FcbArchiveBatchMutationPlanResult plan = await
            FcbArchiveBatchMutationPlanService.CreateAsync(
                target,
                0,
                0x0123456789ABCDEF,
                schema,
                mutations,
                token);

        Assert.Equal(2, plan.Mutations.Count);
        Assert.False(plan.NoOp);
        Assert.Equal("true", plan.Mutations[0].CurrentValue);
        Assert.Equal("false", plan.Mutations[0].RequestedValue);
        Assert.Equal("false", plan.Mutations[1].CurrentValue);
        Assert.Equal("true", plan.Mutations[1].RequestedValue);

        var copyPair = new ArchivePair(
            Path.Combine(directory, "batch-copy.fat"),
            Path.Combine(directory, "batch-copy.dat"));
        FcbArchiveBatchMutationCopyResult copy = await
            FcbArchiveBatchMutationCopyService.CreateAsync(
                target,
                copyPair,
                0,
                0x0123456789ABCDEF,
                schema,
                mutations,
                directory,
                token);
        Assert.True(copy.PayloadExact);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalData, await File.ReadAllBytesAsync(target.DatPath, token));
        using (FileStream copyFat = File.OpenRead(copyPair.FatPath))
        {
            FatV10Entry copyEntry = FatV10IndexReader.Read(
                copyFat,
                new FileInfo(copyPair.DatPath).Length).Entries[0];
            await using FileStream copyData = File.OpenRead(copyPair.DatPath);
            await using var copyPayload = new MemoryStream();
            await FatV10PayloadExtractor.ExtractAsync(copyData, copyEntry, copyPayload, token);
            copyPayload.Position = 0;
            FcbDocument copied = FcbReader.Read(copyPayload);
            Assert.Equal<byte>([0], copied.Root.Fields[0].Data.ToArray());
            Assert.Equal<byte>([1], copied.Root.Fields[1].Data.ToArray());
        }

        FcbArchiveBatchMutationApplyResult result = await
            FcbArchiveBatchMutationApplyService.ApplyAsync(
                target,
                0,
                0x0123456789ABCDEF,
                plan.SourcePayloadSha256,
                schema,
                mutations,
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
        FcbDocument published = FcbReader.Read(payload);
        Assert.Equal<byte>([0], published.Root.Fields[0].Data.ToArray());
        Assert.Equal<byte>([1], published.Root.Fields[1].Data.ToArray());
        Assert.Empty(Directory.EnumerateDirectories(directory, "fcb-batch-dryrun-*"));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.rollback-*.tmp"));
    }

    [Fact]
    public async Task MutationManifestProducesAByteExactNoOpBatchTemplate()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateBooleanFieldsFcb(true, false);
        ArchivePair source = CreateArchive(fcb.Length, [.. fcb, 0xAA, 0xBB]);
        using var schemaInput = new StringReader(
            "00000010 00000020 Boolean\n00000010 00000021 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbArchiveMutationManifestResult manifest = await
            FcbArchiveMutationManifestService.CreateAsync(
                source,
                0,
                0x0123456789ABCDEF,
                schema,
                token);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(fcb)), manifest.SourcePayloadSha256);
        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal(0, manifest.ReferencedFieldCount);
        Assert.Collection(
            manifest.Entries,
            first =>
            {
                Assert.Equal((0, 0), (first.NodeIndex, first.FieldIndex));
                Assert.Equal((0x10U, 0x20U), (first.TypeHash, first.FieldHash));
                Assert.Equal(FcbValueKind.Boolean, first.Codec);
                Assert.Equal("true", first.Value);
            },
            second =>
            {
                Assert.Equal((0, 1), (second.NodeIndex, second.FieldIndex));
                Assert.Equal((0x10U, 0x21U), (second.TypeHash, second.FieldHash));
                Assert.Equal(FcbValueKind.Boolean, second.Codec);
                Assert.Equal("false", second.Value);
            });

        FcbArchiveFieldMutation[] noOpMutations = manifest.Entries
            .Select(entry => new FcbArchiveFieldMutation(
                entry.NodeIndex,
                entry.FieldIndex,
                entry.TypeHash,
                entry.FieldHash,
                entry.Value))
            .ToArray();
        FcbArchiveBatchMutationPlanResult plan = await
            FcbArchiveBatchMutationPlanService.CreateAsync(
                source,
                0,
                0x0123456789ABCDEF,
                schema,
                noOpMutations,
                token);

        Assert.True(plan.NoOp);
        Assert.Equal(plan.SourcePayloadSha256, plan.PlannedPayloadSha256);
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

    private static byte[] CreateBooleanFieldsFcb(bool first, bool second)
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(2);
        WriteUInt32(body, 0x20);
        body.WriteByte(1);
        body.WriteByte(first ? (byte)1 : (byte)0);
        WriteUInt32(body, 0x21);
        body.WriteByte(1);
        body.WriteByte(second ? (byte)1 : (byte)0);
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
