using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbMutationTsvCodecTests
{
    [Fact]
    public void EscapedValueRoundTripsWithoutChangingTsvStructure()
    {
        const string value = "path\\segment\tline\r\nnext";

        string escaped = FcbMutationTsvCodec.EscapeValue(value);

        Assert.Equal("path\\\\segment\\tline\\r\\nnext", escaped);
        Assert.DoesNotContain('\t', escaped);
        Assert.DoesNotContain('\r', escaped);
        Assert.DoesNotContain('\n', escaped);
        Assert.Equal(value, FcbMutationTsvCodec.UnescapeValue(escaped));
    }

    [Fact]
    public void UnescapeValueRejectsUnknownOrIncompleteEscape()
    {
        Assert.Throws<FormatException>(() => FcbMutationTsvCodec.UnescapeValue("value\\x"));
        Assert.Throws<FormatException>(() => FcbMutationTsvCodec.UnescapeValue("value\\"));
    }
}
