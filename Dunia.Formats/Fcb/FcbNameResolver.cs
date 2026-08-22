using Dunia.Formats.Hashing;
using System.Globalization;
using System.Xml;

namespace Dunia.Formats.Fcb;

public sealed class FcbNameResolver
{
    private readonly Dictionary<uint, List<string>> names = [];

    public int NameCount { get; private set; }

    public int HashCount => names.Count;

    public static FcbNameResolver Load(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolver = new FcbNameResolver();
        string? line;
        int lineNumber = 0;
        while ((line = input.ReadLine()) is not null)
        {
            lineNumber++;
            string candidate = line.Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#') || candidate.StartsWith(';'))
            {
                continue;
            }

            int separator = candidate.IndexOf('\t');
            if (separator < 0)
            {
                resolver.Add(candidate);
                continue;
            }

            ReadOnlySpan<char> rawHash = candidate.AsSpan(0, separator);
            string name = candidate[(separator + 1)..];
            if (rawHash.Length != 8
                || !uint.TryParse(rawHash, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint hash)
                || string.IsNullOrWhiteSpace(name)
                || DuniaCrc32.Compute(name) != hash)
            {
                throw new InvalidDataException($"Invalid FCB name mapping at line {lineNumber}.");
            }

            resolver.Add(name);
        }

        return resolver;
    }

    public static FcbNameResolver LoadDefinitionsXml(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolver = new FcbNameResolver();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            using XmlReader reader = XmlReader.Create(input, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name is not ("class" or "member"))
                {
                    continue;
                }

                string? name = reader.GetAttribute("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    resolver.Add(name);
                }
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Invalid FCB definitions XML.", ex);
        }

        return resolver;
    }

    public void Add(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        uint hash = DuniaCrc32.Compute(name);
        if (!names.TryGetValue(hash, out List<string>? candidates))
        {
            candidates = [];
            names.Add(hash, candidates);
        }

        if (!candidates.Contains(name, StringComparer.Ordinal))
        {
            candidates.Add(name);
            NameCount++;
        }
    }

    public IReadOnlyList<string> Resolve(uint hash) =>
        names.TryGetValue(hash, out List<string>? candidates)
            ? candidates.AsReadOnly()
            : Array.Empty<string>();
}
