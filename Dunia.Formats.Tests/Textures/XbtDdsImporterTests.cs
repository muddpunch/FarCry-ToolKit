using System.Buffers.Binary;
using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtDdsImporterTests
{
    [Fact]
    public async Task ImportAsyncPreservesWrapperAndReplacesDdsPayload()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] templateDds = CreateDds(4, 4, 71, 0x11);
        byte[] replacementDds = CreateDds(4, 4, 71, 0xA5);
        byte[] wrapper = CreateWrapper(36);
        using var output = new MemoryStream();

        XbtDdsImportResult result = await XbtDdsImporter.ImportAsync(
            new MemoryStream([.. wrapper, .. templateDds]),
            new MemoryStream(replacementDds),
            output,
            token);

        Assert.Equal(wrapper.Length, result.HeaderLength);
        Assert.Equal(replacementDds.Length, result.DdsLength);
        Assert.Equal<byte>([.. wrapper, .. replacementDds], output.ToArray());
    }

    [Fact]
    public async Task ImportAsyncRejectsLayoutMismatchBeforeWriting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] template = [.. CreateWrapper(36), .. CreateDds(4, 4, 71, 0x11)];
        using var output = new MemoryStream();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtDdsImporter.ImportAsync(
                new MemoryStream(template),
                new MemoryStream(CreateDds(8, 4, 71, 0xA5)),
                output,
                token));

        Assert.Contains("layout does not match", exception.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ImportAsyncRejectsLengthMismatchBeforeWriting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] templateDds = CreateDds(4, 4, 71, 0x11);
        byte[] replacementDds = [.. CreateDds(4, 4, 71, 0xA5), 0x00];
        byte[] template = [.. CreateWrapper(36), .. templateDds];
        using var output = new MemoryStream();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtDdsImporter.ImportAsync(
                new MemoryStream(template),
                new MemoryStream(replacementDds),
                output,
                token));

        Assert.Contains("length", exception.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ImportAsyncRejectsBareDdsTemplate()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtDdsImporter.ImportAsync(
                new MemoryStream(CreateDds(4, 4, 71, 0x11)),
                new MemoryStream(CreateDds(4, 4, 71, 0xA5)),
                output,
                token));

        Assert.Equal(0, output.Length);
    }

    private static byte[] CreateWrapper(int length)
    {
        byte[] result = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x00584254);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 116);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), length);
        result.AsSpan(12).Fill(0x5A);
        return result;
    }

    private static byte[] CreateDds(uint width, uint height, uint dxgiFormat, byte fill)
    {
        byte[] result = new byte[164];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), height);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(80), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(84), 0x30315844);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(128), dxgiFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(132), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(140), 1);
        result.AsSpan(148).Fill(fill);
        return result;
    }
}
