namespace Dunia.Formats.Hashing;

public static class DuniaPathHash
{
    public static ulong Compute(string path) => DuniaCrc64.Compute(Normalize(path));

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim().Replace('/', '\\').ToLowerInvariant();
    }
}
