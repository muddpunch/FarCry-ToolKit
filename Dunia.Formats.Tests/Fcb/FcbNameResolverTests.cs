using Dunia.Formats.Fcb;
using Dunia.Formats.Hashing;
using System.Text;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbNameResolverTests
{
    [Fact]
    public void LoadPreservesCaseSkipsCommentsAndDeduplicatesExactNames()
    {
        using var input = new StringReader("# comment\nTypeName\ntypename\nTypeName\n; ignored\n");

        FcbNameResolver resolver = FcbNameResolver.Load(input);

        Assert.Equal(2, resolver.NameCount);
        Assert.Equal(2, resolver.HashCount);
        Assert.Equal("TypeName", Assert.Single(resolver.Resolve(DuniaCrc32.Compute("TypeName"))));
        Assert.Equal("typename", Assert.Single(resolver.Resolve(DuniaCrc32.Compute("typename"))));
    }

    [Fact]
    public void CoverageReportsUnknownHashesWithoutInventingNames()
    {
        var resolver = new FcbNameResolver();
        resolver.Add("Known");
        uint known = DuniaCrc32.Compute("Known");

        FcbNameCoverageReport report = FcbNameCoverageAnalyzer.Analyze([known, 0x12345678, known], resolver);

        Assert.Equal(3, report.OccurrenceCount);
        Assert.Equal(2, report.ResolvedCount);
        Assert.Equal(1, report.UnknownCount);
        Assert.Equal(new uint[] { 0x12345678 }, report.UnknownHashes);
    }

    [Fact]
    public void LoadDefinitionsXmlReadsOnlyClassAndMemberNames()
    {
        const string xml = "<classes><class name='Type'><member name='Field'>UInt32</member></class><other name='Ignored'/></classes>";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        FcbNameResolver resolver = FcbNameResolver.LoadDefinitionsXml(input);

        Assert.Equal(2, resolver.NameCount);
        Assert.Equal("Type", Assert.Single(resolver.Resolve(DuniaCrc32.Compute("Type"))));
        Assert.Equal("Field", Assert.Single(resolver.Resolve(DuniaCrc32.Compute("Field"))));
        Assert.Empty(resolver.Resolve(DuniaCrc32.Compute("Ignored")));
    }

    [Fact]
    public void LoadDefinitionsXmlRejectsDtd()
    {
        const string xml = "<!DOCTYPE classes [<!ENTITY xxe SYSTEM 'file:///secret'>]><classes/>";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Throws<InvalidDataException>(() => FcbNameResolver.LoadDefinitionsXml(input));
    }
}
