namespace Dunia.Formats.Archives;

public sealed record ArchivePair
{
    public ArchivePair(string fatPath, string datPath)
    {
        FatPath = Path.GetFullPath(RequirePath(fatPath, nameof(fatPath)));
        DatPath = Path.GetFullPath(RequirePath(datPath, nameof(datPath)));
    }

    public string FatPath { get; }

    public string DatPath { get; }

    public static ArchivePair FromIndex(string fatPath)
    {
        string fullFatPath = Path.GetFullPath(RequirePath(fatPath, nameof(fatPath)));
        return new(fullFatPath, Path.ChangeExtension(fullFatPath, ".dat"));
    }

    private static string RequirePath(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Path cannot be empty.", paramName)
            : value;
}

