using System.Buffers.Binary;

namespace Dunia.Formats.Fcb;

public static class FcbPackedCountWriter
{
    public static void Write(Stream output, FcbPackedCount count)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (count.IsOffset)
        {
            output.WriteByte(0xFE);
            WriteUInt32(output, count.Value);
            return;
        }

        if (count.Value < 0xFE)
        {
            output.WriteByte((byte)count.Value);
            return;
        }

        output.WriteByte(0xFF);
        WriteUInt32(output, count.Value);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
