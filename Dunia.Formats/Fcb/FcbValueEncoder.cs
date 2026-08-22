using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Dunia.Formats.Fcb;

public static class FcbValueEncoder
{
    public static byte[] Encode(FcbValueKind codec, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return codec switch
        {
            FcbValueKind.AsciiNullTerminated => EncodeAscii(value),
            FcbValueKind.Boolean => [ParseBoolean(value) ? (byte)1 : (byte)0],
            FcbValueKind.Unsigned8Bit => [ParseInteger<byte>(value)],
            FcbValueKind.Signed16Bit => EncodeInt16(ParseInteger<short>(value)),
            FcbValueKind.Unsigned16Bit => EncodeUInt16(ParseInteger<ushort>(value)),
            FcbValueKind.Signed32Bit => EncodeInt32(ParseInteger<int>(value)),
            FcbValueKind.Unsigned32Bit => EncodeUInt32(ParseInteger<uint>(value)),
            FcbValueKind.Crc32Hash => EncodeUInt32(ParseHash32(value)),
            FcbValueKind.Ieee754Binary32 => EncodeSingle(ParseSingle(value)),
            FcbValueKind.Signed64Bit => EncodeInt64(ParseInteger<long>(value)),
            FcbValueKind.Unsigned64Bit => EncodeUInt64(ParseInteger<ulong>(value)),
            FcbValueKind.Crc64Hash => EncodeUInt64(ParseHash64(value)),
            FcbValueKind.Ieee754Binary64 => EncodeDouble(ParseDouble(value)),
            FcbValueKind.Vector2Binary32 => EncodeVector(value, 2),
            FcbValueKind.Vector3Binary32 => EncodeVector(value, 3),
            FcbValueKind.Vector4Binary32 => EncodeVector(value, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported FCB value codec."),
        };
    }

    private static byte[] EncodeAscii(string value)
    {
        if (value.Any(character => character is < ' ' or > '~'))
        {
            throw new FormatException("ASCII value must contain only printable characters.");
        }

        byte[] result = new byte[checked(value.Length + 1)];
        Encoding.ASCII.GetBytes(value, result);
        return result;
    }

    private static bool ParseBoolean(string value) => value switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new FormatException("Boolean value must be true, false, 1, or 0."),
    };

    private static T ParseInteger<T>(string value) where T : IParsable<T>
    {
        if (!T.TryParse(value, CultureInfo.InvariantCulture, out T? result))
        {
            throw new FormatException($"Invalid {typeof(T).Name} value.");
        }

        return result;
    }

    private static float ParseSingle(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            || !float.IsFinite(result))
        {
            throw new FormatException("Invalid finite binary32 value.");
        }

        return result;
    }

    private static double ParseDouble(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            || !double.IsFinite(result))
        {
            throw new FormatException("Invalid finite binary64 value.");
        }

        return result;
    }

    private static uint ParseHash32(string value) => checked((uint)ParseHash(value, 8));

    private static ulong ParseHash64(string value) => ParseHash(value, 16);

    private static ulong ParseHash(string value, int digits)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        if (span.Length != digits
            || !ulong.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong result))
        {
            throw new FormatException($"Hash value must contain exactly {digits} hexadecimal digits.");
        }

        return result;
    }

    private static byte[] EncodeVector(string value, int componentCount)
    {
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '[' && span[^1] == ']')
        {
            span = span[1..^1];
        }

        string[] components = span.ToString().Split(',', StringSplitOptions.TrimEntries);
        if (components.Length != componentCount)
        {
            throw new FormatException($"Vector requires exactly {componentCount} comma-separated components.");
        }

        byte[] result = new byte[componentCount * sizeof(float)];
        for (int i = 0; i < componentCount; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * sizeof(float)), ParseSingle(components[i]));
        }

        return result;
    }

    private static byte[] EncodeInt16(short value)
    {
        byte[] result = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        byte[] result = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeInt32(int value)
    {
        byte[] result = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeUInt32(uint value)
    {
        byte[] result = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeSingle(float value)
    {
        byte[] result = new byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeInt64(long value)
    {
        byte[] result = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeUInt64(ulong value)
    {
        byte[] result = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(result, value);
        return result;
    }

    private static byte[] EncodeDouble(double value)
    {
        byte[] result = new byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleLittleEndian(result, value);
        return result;
    }
}
