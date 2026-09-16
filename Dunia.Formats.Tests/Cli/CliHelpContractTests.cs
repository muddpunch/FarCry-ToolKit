using Dunia.Cli;

namespace Dunia.Formats.Tests.Cli;

public sealed class CliHelpContractTests
{
    private static readonly string[] RequiredTopLevelCommands =
    [
        "probe", "list", "entry", "get", "tex", "mips", "mesh", "fcb", "pack", "rebuild", "refs", "hash", "verify",
    ];

    [Fact]
    public void HelpAdvertisesCompletedPhaseFourSurfaceWithoutPlaceholders()
    {
        string help = Program.HelpText;

        Assert.DoesNotContain("placeholder", help, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not implemented", help, StringComparison.OrdinalIgnoreCase);
        foreach (string command in RequiredTopLevelCommands)
        {
            Assert.Contains($"  {command} ", help, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("tex extract")]
    [InlineData("tex export-png")]
    [InlineData("tex import")]
    [InlineData("tex import-png")]
    [InlineData("mips list")]
    [InlineData("mips export")]
    [InlineData("mesh probe")]
    [InlineData("mesh export-fbx")]
    [InlineData("verify roundtrip")]
    [InlineData("verify replacement")]
    public void HelpRetainsImplementedSubcommands(string command) =>
        Assert.Contains($"dunia {command} ", Program.HelpText, StringComparison.Ordinal);
}
