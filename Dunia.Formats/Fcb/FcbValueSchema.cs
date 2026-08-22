using System.Globalization;

namespace Dunia.Formats.Fcb;

public sealed class FcbValueSchema
{
    private readonly Dictionary<FcbValueSchemaKey, FcbValueKind> codecs = [];

    public int Count => codecs.Count;

    public static FcbValueSchema Load(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var schema = new FcbValueSchema();
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

            string[] parts = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3
                || !TryParseHash(parts[0], out uint nodeTypeHash)
                || !TryParseHash(parts[1], out uint fieldHash)
                || !Enum.TryParse(parts[2], true, out FcbValueKind codec)
                || !Enum.IsDefined(codec))
            {
                throw new InvalidDataException($"Invalid FCB value schema entry at line {lineNumber}.");
            }

            var key = new FcbValueSchemaKey(nodeTypeHash, fieldHash);
            if (!schema.codecs.TryAdd(key, codec))
            {
                throw new InvalidDataException($"Duplicate FCB value schema entry at line {lineNumber}.");
            }
        }

        return schema;
    }

    public bool TryResolve(uint nodeTypeHash, uint fieldHash, out FcbValueKind codec) =>
        codecs.TryGetValue(new(nodeTypeHash, fieldHash), out codec);

    private static bool TryParseHash(string value, out uint hash)
    {
        hash = 0;
        ReadOnlySpan<char> span = value.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        return span.Length == 8
            && uint.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hash);
    }
}
