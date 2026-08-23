namespace Dunia.Formats.Hashing;

public sealed class DuniaNameResolver
{
    private readonly Dictionary<ulong, List<string>> _names = [];

    public int NameCount { get; private set; }

    public int HashCount => _names.Count;

    public static DuniaNameResolver Load(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolver = new DuniaNameResolver();
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            string candidate = line.Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#') || candidate.StartsWith(';'))
            {
                continue;
            }

            resolver.Add(candidate);
        }

        return resolver;
    }

    public void Add(string path)
    {
        string normalized = DuniaPathHash.Normalize(path);
        ulong hash = DuniaCrc64.Compute(normalized);
        if (!_names.TryGetValue(hash, out List<string>? candidates))
        {
            candidates = [];
            _names.Add(hash, candidates);
        }

        if (!candidates.Contains(normalized, StringComparer.Ordinal))
        {
            candidates.Add(normalized);
            NameCount++;
        }
    }

    public IReadOnlyList<string> Resolve(ulong hash) =>
        _names.TryGetValue(hash, out List<string>? candidates)
            ? candidates
            : Array.Empty<string>();
}
