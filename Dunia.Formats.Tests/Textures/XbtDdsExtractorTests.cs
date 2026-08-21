using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtDdsExtractorTests
{
    [Fact]
    public async Task ExtractAsyncStripsHeaderAndPreservesDdsBytes()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] dds = [0x44, 0x44, 0x53, 0x20, 0x01, 0x80, 0xFF];
        byte[] xbt = [0x10, 0x20, 0x30, .. dds];
        using var output = new MemoryStream();

        XbtDdsExtractionResult result = await XbtDdsExtractor.ExtractAsync(
            new MemoryStream(xbt),
            output,
            token);

        Assert.Equal(3, result.HeaderLength);
        Assert.Equal(dds.Length, result.DdsLength);
        Assert.Equal(dds, output.ToArray());
    }

    [Fact]
    public async Task ExtractAsyncAcceptsBareDdsInput()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] dds = [0x44, 0x44, 0x53, 0x20, 0x01];
        using var output = new MemoryStream();

        XbtDdsExtractionResult result = await XbtDdsExtractor.ExtractAsync(
            new MemoryStream(dds),
            output,
            token);

        Assert.Equal(0, result.HeaderLength);
        Assert.Equal(dds, output.ToArray());
    }

    [Fact]
    public async Task ExtractAsyncFindsMagicAcrossReadBoundary()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] xbt = new byte[(80 * 1024) + 5];
        int offset = (80 * 1024) - 2;
        "DDS "u8.CopyTo(xbt.AsSpan(offset));
        xbt[^1] = 0x7F;
        using var output = new MemoryStream();

        XbtDdsExtractionResult result = await XbtDdsExtractor.ExtractAsync(
            new MemoryStream(xbt),
            output,
            token);

        Assert.Equal(offset, result.HeaderLength);
        Assert.Equal<byte>([0x44, 0x44, 0x53, 0x20, 0x00, 0x00, 0x7F], output.ToArray());
    }

    [Fact]
    public async Task ExtractAsyncRejectsMissingPayloadWithoutWritingOutput()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtDdsExtractor.ExtractAsync(new MemoryStream(new byte[32]), output, token));

        Assert.Equal(0, output.Length);
    }
}
