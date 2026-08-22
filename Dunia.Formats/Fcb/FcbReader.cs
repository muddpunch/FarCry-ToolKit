using System.Buffers.Binary;

namespace Dunia.Formats.Fcb;

// Layout adapted from Gibbed.Dunia's zlib-licensed BinaryResourceFile.
public static class FcbReader
{
    public const uint Signature = 0x4643626E;
    public const ushort Version = 2;
    public const int HeaderSize = 16;
    private const int MaxDepth = 1024;

    public static FcbDocument Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("FCB input must be readable and seekable.", nameof(input));
        }

        long originalPosition = input.Position;
        try
        {
            input.Position = 0;
            FcbHeader header = ReadHeader(input);
            var nodes = new List<FcbNode>();
            var inlineFields = new Dictionary<long, FcbField>();
            int fieldCount = 0;
            FcbNode root = ReadNode(input, nodes, inlineFields, ref fieldCount, 0);
            if (input.Position != input.Length)
            {
                throw new InvalidDataException(
                    $"FCB contains {input.Length - input.Position} unsupported trailing bytes.");
            }

            return new(header, root, nodes.Count, fieldCount);
        }
        finally
        {
            input.Position = originalPosition;
        }
    }

    private static FcbHeader ReadHeader(Stream input)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        input.ReadExactly(header);
        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(header);
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        if (signature != Signature)
        {
            throw new InvalidDataException("Invalid FCbn signature.");
        }

        if (version != Version)
        {
            throw new InvalidDataException($"Expected FCB v2, got v{version}.");
        }

        if (flags != 0)
        {
            throw new NotSupportedException($"Unsupported FCB flags: 0x{flags:X4}.");
        }

        return new(
            version,
            flags,
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[12..]));
    }

    private static FcbNode ReadNode(
        Stream input,
        List<FcbNode> nodes,
        Dictionary<long, FcbField> inlineFields,
        ref int fieldCount,
        int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException("FCB node nesting exceeds the supported limit.");
        }

        long nodeOffset = input.Position;
        FcbPackedCount childCount = FcbPackedCountReader.Read(input);
        if (childCount.IsOffset)
        {
            if (childCount.Value >= nodes.Count)
            {
                throw new InvalidDataException($"FCB node pointer {childCount.Value} is out of range.");
            }

            return nodes[(int)childCount.Value];
        }

        var node = new FcbNode(nodeOffset);
        nodes.Add(node);
        node.TypeHash = ReadUInt32(input);
        FcbPackedCount valueCount = FcbPackedCountReader.Read(input);
        if (valueCount.IsOffset)
        {
            throw new InvalidDataException("FCB value count cannot be an offset.");
        }

        for (uint i = 0; i < valueCount.Value; i++)
        {
            uint nameHash = ReadUInt32(input);
            long valueOffset = input.Position;
            FcbPackedCount size = FcbPackedCountReader.Read(input);
            FcbField field;
            if (size.IsOffset)
            {
                long targetOffset = checked(valueOffset - size.Value);
                if (!inlineFields.TryGetValue(targetOffset, out FcbField? target))
                {
                    throw new InvalidDataException(
                        $"FCB value reference at {valueOffset} targets unknown offset {targetOffset}.");
                }

                field = new(nameHash, target.Data.ToArray(), valueOffset, target);
            }
            else
            {
                if (size.Value > int.MaxValue || size.Value > input.Length - input.Position)
                {
                    throw new InvalidDataException("FCB value exceeds the input bounds.");
                }

                byte[] data = GC.AllocateUninitializedArray<byte>((int)size.Value);
                input.ReadExactly(data);
                field = new(nameHash, data, valueOffset, null);
                inlineFields.Add(valueOffset, field);
            }

            node.MutableFields.Add(field);
            fieldCount = checked(fieldCount + 1);
        }

        if (childCount.Value > int.MaxValue)
        {
            throw new InvalidDataException("FCB child count exceeds the supported limit.");
        }

        for (uint i = 0; i < childCount.Value; i++)
        {
            node.MutableChildren.Add(ReadNode(input, nodes, inlineFields, ref fieldCount, depth + 1));
        }

        return node;
    }

    private static uint ReadUInt32(Stream input)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        input.ReadExactly(data);
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }
}
