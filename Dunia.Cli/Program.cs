namespace Dunia.Cli;

internal static class Program
{
    private const string Usage = """
        Dunia Toolkit CLI

        Usage:
          dunia <command> [options]

        Commands:
          list      List archive entries
          entry     Inspect an archive entry
          get       Extract an archive entry
          tex       Convert or replace a texture
          pack      Pack changed resources
          rebuild   Rebuild an archive pair
          refs      Resolve resource references
          hash      Compute or resolve Dunia hashes
        """;

    public static int Main(string[] args)
    {
        Console.WriteLine(Usage);
        return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 2;
    }
}

