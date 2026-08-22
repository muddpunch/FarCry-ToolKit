using System.Buffers.Binary;

namespace Dunia.Formats.Fcb;

public static class FcbWriter
{
    public static void Write(Stream output, FcbDocument document)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        if (!output.CanWrite || !output.CanSeek)
        {
            throw new ArgumentException("FCB output must be writable and seekable.", nameof(output));
        }

        if (output.Position != 0 || output.Length != 0)
        {
            throw new ArgumentException("FCB output must be empty and positioned at zero.", nameof(output));
        }

        ValidateHeader(document.Header);
        WriteHeader(output, document.Header);

        var nodes = new Dictionary<FcbNode, uint>(ReferenceEqualityComparer.Instance);
        var fields = new Dictionary<FcbField, long>(ReferenceEqualityComparer.Instance);
        WriteNode(output, document.Root, nodes, fields);
    }

    private static void ValidateHeader(FcbHeader header)
    {
        if (header.Version != FcbReader.Version)
        {
            throw new NotSupportedException($"Unsupported FCB version: {header.Version}.");
        }

        if (header.Flags != 0)
        {
            throw new NotSupportedException($"Unsupported FCB flags: 0x{header.Flags:X4}.");
        }
    }

    private static void WriteHeader(Stream output, FcbHeader header)
    {
        Span<byte> data = stackalloc byte[FcbReader.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data[4..], header.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(data[6..], header.Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(data[8..], header.DeclaredObjectCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data[12..], header.DeclaredValueCount);
        output.Write(data);
    }

    private static void WriteNode(
        Stream output,
        FcbNode node,
        Dictionary<FcbNode, uint> nodes,
        Dictionary<FcbField, long> fields)
    {
        if (nodes.TryGetValue(node, out uint pointer))
        {
            FcbPackedCountWriter.Write(output, new(pointer, true));
            return;
        }

        uint nodeIndex = checked((uint)nodes.Count);
        nodes.Add(node, nodeIndex);
        FcbPackedCountWriter.Write(output, new(checked((uint)node.Children.Count), false));
        WriteUInt32(output, node.TypeHash);
        FcbPackedCountWriter.Write(output, new(checked((uint)node.Fields.Count), false));

        foreach (FcbField field in node.Fields)
        {
            WriteUInt32(output, field.NameHash);
            long valueOffset = output.Position;
            if (field.ReferenceTarget is null)
            {
                fields.Add(field, valueOffset);
                FcbPackedCountWriter.Write(output, new(checked((uint)field.Data.Length), false));
                output.Write(field.Data.Span);
                continue;
            }

            if (!fields.TryGetValue(field.ReferenceTarget, out long targetOffset) || targetOffset >= valueOffset)
            {
                throw new InvalidDataException("FCB value reference must target an earlier inline field.");
            }

            uint distance = checked((uint)(valueOffset - targetOffset));
            FcbPackedCountWriter.Write(output, new(distance, true));
        }

        foreach (FcbNode child in node.Children)
        {
            WriteNode(output, child, nodes, fields);
        }
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
