using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Dunia.Formats.Fcb;

public static class FcbValueProjector
{
    public static FcbValueProjection Project(FcbField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        ReadOnlySpan<byte> data = field.Data.Span;
        var candidates = new List<FcbValueCandidate>();

        if (IsNullTerminatedAscii(data))
        {
            string value = Encoding.ASCII.GetString(data[..^1]);
            candidates.Add(new(FcbValueKind.AsciiNullTerminated, FcbValueEvidence.Structural, value));
        }

        switch (data.Length)
        {
            case 1:
                if (data[0] is 0 or 1)
                {
                    candidates.Add(new(
                        FcbValueKind.Boolean,
                        FcbValueEvidence.SizeCompatible,
                        (data[0] != 0).ToString().ToLowerInvariant()));
                }

                candidates.Add(new(FcbValueKind.Unsigned8Bit, FcbValueEvidence.SizeCompatible, Format(data[0])));
                break;
            case 2:
                candidates.Add(new(
                    FcbValueKind.Signed16Bit,
                    FcbValueEvidence.SizeCompatible,
                    Format(BinaryPrimitives.ReadInt16LittleEndian(data))));
                candidates.Add(new(
                    FcbValueKind.Unsigned16Bit,
                    FcbValueEvidence.SizeCompatible,
                    Format(BinaryPrimitives.ReadUInt16LittleEndian(data))));
                break;
            case 4:
                int int32 = BinaryPrimitives.ReadInt32LittleEndian(data);
                candidates.Add(new(FcbValueKind.Signed32Bit, FcbValueEvidence.SizeCompatible, Format(int32)));
                candidates.Add(new(
                    FcbValueKind.Unsigned32Bit,
                    FcbValueEvidence.SizeCompatible,
                    Format(BinaryPrimitives.ReadUInt32LittleEndian(data))));
                candidates.Add(new(
                    FcbValueKind.Crc32Hash,
                    FcbValueEvidence.SizeCompatible,
                    BinaryPrimitives.ReadUInt32LittleEndian(data).ToString("X8", CultureInfo.InvariantCulture)));
                candidates.Add(new(
                    FcbValueKind.Ieee754Binary32,
                    FcbValueEvidence.SizeCompatible,
                    Format(BitConverter.Int32BitsToSingle(int32))));
                break;
            case 8:
                long int64 = BinaryPrimitives.ReadInt64LittleEndian(data);
                candidates.Add(new(FcbValueKind.Signed64Bit, FcbValueEvidence.SizeCompatible, Format(int64)));
                candidates.Add(new(
                    FcbValueKind.Unsigned64Bit,
                    FcbValueEvidence.SizeCompatible,
                    Format(BinaryPrimitives.ReadUInt64LittleEndian(data))));
                candidates.Add(new(
                    FcbValueKind.Crc64Hash,
                    FcbValueEvidence.SizeCompatible,
                    BinaryPrimitives.ReadUInt64LittleEndian(data).ToString("X16", CultureInfo.InvariantCulture)));
                candidates.Add(new(
                    FcbValueKind.Ieee754Binary64,
                    FcbValueEvidence.SizeCompatible,
                    Format(BitConverter.Int64BitsToDouble(int64))));
                candidates.Add(new(
                    FcbValueKind.Vector2Binary32,
                    FcbValueEvidence.SizeCompatible,
                    FormatVector(data, 2)));
                break;
            case 12:
                candidates.Add(new(
                    FcbValueKind.Vector3Binary32,
                    FcbValueEvidence.SizeCompatible,
                    FormatVector(data, 3)));
                break;
            case 16:
                candidates.Add(new(
                    FcbValueKind.Vector4Binary32,
                    FcbValueEvidence.SizeCompatible,
                    FormatVector(data, 4)));
                break;
        }

        return new(Convert.ToHexString(data), candidates.AsReadOnly());
    }

    private static bool IsNullTerminatedAscii(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data[^1] != 0)
        {
            return false;
        }

        foreach (byte value in data[..^1])
        {
            if (value is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatVector(ReadOnlySpan<byte> data, int count)
    {
        var values = new string[count];
        for (int i = 0; i < count; i++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(data[(i * sizeof(float))..]);
            values[i] = Format(BitConverter.Int32BitsToSingle(bits));
        }

        return $"[{string.Join(',', values)}]";
    }

    private static string Format<T>(T value) where T : IFormattable =>
        value.ToString(value is float or double ? "R" : null, CultureInfo.InvariantCulture);
}
