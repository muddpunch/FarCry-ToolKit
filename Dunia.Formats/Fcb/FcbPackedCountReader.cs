using System.Buffers.Binary;

namespace Dunia.Formats.Fcb;

public static class FcbPackedCountReader
{
    public static FcbPackedCount Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        int marker = input.ReadByte();
        if (marker < 0)
        {
            throw new EndOfStreamException("FCB packed count is truncated.");
        }

        if (marker < 0xFE)
        {
            return new((uint)marker, false);
        }

        Span<byte> data = stackalloc byte[sizeof(uint)];
        input.ReadExactly(data);
        return new(BinaryPrimitives.ReadUInt32LittleEndian(data), marker == 0xFE);
    }
}
