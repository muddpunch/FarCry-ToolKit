using System.Text;

namespace Dunia.Formats.Hashing;

public static class DuniaNameCatalogStore
{
    public static async Task<int> MergeAsync(
        string path,
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(names);

        string fullPath = Path.GetFullPath(path);
        var merged = new HashSet<string>(StringComparer.Ordinal);

        if (File.Exists(fullPath))
        {
            await ReadIntoAsync(fullPath, merged, cancellationToken).ConfigureAwait(false);
        }

        foreach (string name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddNormalized(merged, name);
        }

        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Name catalog path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllLinesAsync(
                temporaryPath,
                merged.Order(StringComparer.Ordinal),
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return merged.Count;
    }

    private static async Task ReadIntoAsync(
        string path,
        HashSet<string> names,
        CancellationToken cancellationToken)
    {
        using var input = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            AddNormalized(names, line);
        }
    }

    private static void AddNormalized(HashSet<string> names, string value)
    {
        string candidate = value.Trim();
        if (candidate.Length == 0 || candidate.StartsWith('#') || candidate.StartsWith(';'))
        {
            return;
        }

        names.Add(DuniaPathHash.Normalize(candidate));
    }
}
