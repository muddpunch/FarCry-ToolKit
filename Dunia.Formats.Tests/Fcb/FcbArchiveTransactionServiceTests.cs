using System.Buffers.Binary;
using System.Reflection;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbArchiveTransactionServiceTests : IDisposable
{
    private const ulong FirstHash = 0x1111111111111111;
    private const ulong SecondHash = 0x2222222222222222;

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FcbArchiveTransactionServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task TransactionPlansDryRunsCopiesAndAppliesMultipleEntriesAtomically()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] firstFcb = CreateBooleanFcb(true);
        byte[] secondFcb = CreateBooleanFcb(true);
        ArchivePair target = CreateArchive(firstFcb, secondFcb);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        byte[] originalDat = await File.ReadAllBytesAsync(target.DatPath, token);
        FcbValueSchema schema = CreateSchema();
        FcbArchiveTransactionEntry[] requests = CreateRequests();

        FcbArchiveTransactionPlanResult plan = await FcbArchiveTransactionService.PlanAsync(
            target,
            schema,
            requests,
            token);
        FcbArchiveTransactionPlanResult reorderedPlan = await FcbArchiveTransactionService.PlanAsync(
            target,
            schema,
            requests.AsEnumerable().Reverse().ToArray(),
            token);

        Assert.Equal(FcbArchiveTransactionService.ApiVersion, plan.ApiVersion);
        Assert.Equal(2, plan.Entries.Count);
        Assert.False(plan.NoOp);
        Assert.Equal(plan.PlanSha256, reorderedPlan.PlanSha256);
        Assert.Equal("92D8597B337EEE5A65B2DBF5BFF24620F6041A29666A81BCEF2A07EEAB81FDCE", plan.PlanSha256);

        FcbArchiveTransactionDryRunResult dryRun = await FcbArchiveTransactionService.DryRunAsync(
            target,
            schema,
            requests,
            directory,
            token);

        Assert.True(dryRun.IsVerified);
        Assert.Equal(2, dryRun.ReplacementEntryCount);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(target.DatPath, token));

        var copyPair = new ArchivePair(
            Path.Combine(directory, "transaction-copy.fat"),
            Path.Combine(directory, "transaction-copy.dat"));
        FcbArchiveTransactionCopyResult copy = await FcbArchiveTransactionService.CreateCopyAsync(
            target,
            copyPair,
            schema,
            requests,
            directory,
            token);

        Assert.True(copy.PayloadsExact);
        Assert.False(await ReadBooleanAsync(copyPair, 0, token));
        Assert.False(await ReadBooleanAsync(copyPair, 1, token));
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(target.DatPath, token));

        FcbArchiveTransactionApplyResult apply = await FcbArchiveTransactionService.ApplyAsync(
            target,
            schema,
            requests,
            plan.PlanSha256,
            directory,
            token);

        Assert.True(apply.SemanticVerified);
        Assert.False(apply.NoOp);
        Assert.True(Assert.IsType<ArchivePairBackupResult>(apply.Backup).CreatedAny);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath + ".original", token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(target.DatPath + ".original", token));
        Assert.False(await ReadBooleanAsync(target, 0, token));
        Assert.False(await ReadBooleanAsync(target, 1, token));
        Assert.Empty(Directory.EnumerateDirectories(directory, "fcb-transaction-dryrun-*"));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.rollback-*.tmp"));
    }

    [Fact]
    public void ApiVersionOneContractHasExactlyFourOperations()
    {
        Assert.Equal(1, FcbArchiveTransactionService.ApiVersion);
        string[] operations = typeof(FcbArchiveTransactionService)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["ApplyAsync", "CreateCopyAsync", "DryRunAsync", "PlanAsync"],
            operations);
        AssertOperation(
            "PlanAsync",
            typeof(Task<FcbArchiveTransactionPlanResult>),
            typeof(ArchivePair),
            typeof(FcbValueSchema),
            typeof(IReadOnlyList<FcbArchiveTransactionEntry>),
            typeof(CancellationToken));
        AssertOperation(
            "DryRunAsync",
            typeof(Task<FcbArchiveTransactionDryRunResult>),
            typeof(ArchivePair),
            typeof(FcbValueSchema),
            typeof(IReadOnlyList<FcbArchiveTransactionEntry>),
            typeof(string),
            typeof(CancellationToken));
        AssertOperation(
            "CreateCopyAsync",
            typeof(Task<FcbArchiveTransactionCopyResult>),
            typeof(ArchivePair),
            typeof(ArchivePair),
            typeof(FcbValueSchema),
            typeof(IReadOnlyList<FcbArchiveTransactionEntry>),
            typeof(string),
            typeof(CancellationToken));
        AssertOperation(
            "ApplyAsync",
            typeof(Task<FcbArchiveTransactionApplyResult>),
            typeof(ArchivePair),
            typeof(FcbValueSchema),
            typeof(IReadOnlyList<FcbArchiveTransactionEntry>),
            typeof(string),
            typeof(string),
            typeof(CancellationToken));
    }

    [Fact]
    public async Task ApplyRejectsStaleTransactionPlanBeforeBackupOrWrite()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] firstFcb = CreateBooleanFcb(true);
        byte[] secondFcb = CreateBooleanFcb(true);
        ArchivePair target = CreateArchive(firstFcb, secondFcb);
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        byte[] originalDat = await File.ReadAllBytesAsync(target.DatPath, token);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FcbArchiveTransactionService.ApplyAsync(
                target,
                CreateSchema(),
                CreateRequests(),
                new string('0', 64),
                directory,
                token));

        Assert.Contains("Transaction plan SHA-256 mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreateArchive(byte[] first, byte[] second)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        FatV10Entry[] entries =
        [
            new(FirstHash, first.Length, 0, first.Length, FatV10CompressionScheme.None, false),
            new(SecondHash, second.Length, first.Length, second.Length, FatV10CompressionScheme.None, false),
        ];
        using (FileStream fat = File.Create(fatPath))
        {
            FatV10IndexWriter.Write(fat, entries);
        }

        File.WriteAllBytes(datPath, [.. first, .. second]);
        return new(fatPath, datPath);
    }

    private static FcbArchiveTransactionEntry[] CreateRequests() =>
    [
        new(0, FirstHash, [new(0, 0, 0x10, 0x20, "false")]),
        new(1, SecondHash, [new(0, 0, 0x10, 0x20, "false")]),
    ];

    private static FcbValueSchema CreateSchema()
    {
        using var input = new StringReader("00000010 00000020 Boolean\n");
        return FcbValueSchema.Load(input);
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

    private static async Task<bool> ReadBooleanAsync(
        ArchivePair pair,
        int entryIndex,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(pair.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
        }

        await using FileStream data = File.OpenRead(pair.DatPath);
        await using var payload = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(
            data,
            index.Entries[entryIndex],
            payload,
            cancellationToken);
        payload.Position = 0;
        return FcbReader.Read(payload).Root.Fields[0].Data.Span[0] != 0;
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }

    private static void AssertOperation(
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo method = Assert.Single(typeof(FcbArchiveTransactionService)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
, candidate => candidate.Name == name);
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
