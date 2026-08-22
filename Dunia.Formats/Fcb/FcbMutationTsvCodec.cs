using System.Text;

namespace Dunia.Formats.Fcb;

public static class FcbMutationTsvCodec
{
    public static string EscapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\\\",
                '\t' => "\\t",
                '\r' => "\\r",
                '\n' => "\\n",
                _ => character.ToString(),
            });
        }

        return result.ToString();
    }

    public static string UnescapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character != '\\')
            {
                result.Append(character);
                continue;
            }

            if (++i == value.Length)
            {
                throw new FormatException("Mutation value ends with an incomplete escape sequence.");
            }

            result.Append(value[i] switch
            {
                '\\' => '\\',
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                _ => throw new FormatException($"Unsupported mutation value escape sequence: \\{value[i]}"),
            });
        }

        return result.ToString();
    }
}
