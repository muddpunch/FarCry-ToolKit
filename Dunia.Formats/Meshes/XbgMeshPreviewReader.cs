using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace Dunia.Formats.Meshes;

public static class XbgMeshPreviewReader
{
    private const int ChunkHeaderSize = 20;
    private const int MaxFileSize = 512 * 1024 * 1024;
    private const int MaxVertexCount = 1_500_000;
    private const int MaxIndexCount = 6_000_000;
    private const int MaxVertexMetadataSize = 1024 * 1024;

    private static readonly (uint Flag, int Size)[] VertexComponents =
    [
        (0x0001, 12), // Float position.
        (0x0002, 8),  // Quantized Int16 position.
        (0x0004, 8),  // Half position.
        (0x0008, 4),  // UV0.
        (0x0800, 4),  // UV1.
        (0x1000, 4),  // UV2.
        (0x0010, 8),  // Skin weights 0.
        (0x0020, 8),  // Skin weights 1.
        (0x0040, 4),  // Normal.
        (0x0080, 4),  // Color.
        (0x0100, 4),  // Tangent.
        (0x0200, 4),  // Binormal.
        (0x0400, 4),  // FC5 tangent/auxiliary data.
    ];

    public static XbgMeshPreview Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("XBG input must be readable and seekable.", nameof(input));
        }

        if (input.Length > MaxFileSize)
        {
            throw new InvalidDataException("XBG exceeds the 512 MB decode safety limit.");
        }

        input.Position = 0;
        byte[] data = new byte[checked((int)input.Length)];
        input.ReadExactly(data);
        using var validationStream = new MemoryStream(data, writable: false);
        XbgSummary summary = XbgSummaryReader.Read(validationStream);
        XbgChunkInfo geometryChunk = summary.Chunks.Single(chunk => chunk.Name == "SDOL");
        IReadOnlyList<XbgMaterialReference> materials = ReadMaterials(data, summary.Chunks);
        float positionScale = ReadPositionScale(data, summary.Chunks);

        return ReadLods(data, summary, geometryChunk, materials, positionScale);
    }

    private static XbgMeshPreview ReadLods(
        byte[] data,
        XbgSummary summary,
        XbgChunkInfo chunk,
        IReadOnlyList<XbgMaterialReference> materials,
        float positionScale)
    {
        int chunkStart = checked((int)chunk.Offset);
        int chunkEnd = checked(chunkStart + (int)chunk.ChunkSize);
        int cursor = checked(chunkStart + ChunkHeaderSize);
        EnsureAvailable(cursor, 8, chunkEnd, "SDOL header");
        int lodCount = ReadPositiveCount(data, cursor, 64, "LOD");
        cursor += 8; // LOD count + aggregate vertex-buffer count.

        var lods = new XbgMeshLod[lodCount];
        int aggregateVertexCount = 0;
        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            DecodedLod decoded = ReadLod(
                data,
                cursor,
                chunkEnd,
                positionScale,
                expectAnotherLod: lodIndex + 1 < lodCount);
            lods[lodIndex] = decoded.Lod;
            aggregateVertexCount = checked(aggregateVertexCount + decoded.Lod.Positions.Count);
            if (aggregateVertexCount > MaxVertexCount * 2)
            {
                throw new InvalidDataException("XBG aggregate LOD geometry exceeds the decode safety limit.");
            }

            if (lodIndex + 1 < lodCount)
            {
                cursor = FindPlausibleNextLod(data, decoded.EndOffset, chunkEnd) ??
                    throw new InvalidDataException($"XBG LOD {lodIndex + 1} header could not be located safely.");
            }
        }

        return new(summary, materials, Array.AsReadOnly(lods));
    }

    private static DecodedLod ReadLod(
        byte[] data,
        int cursor,
        int chunkEnd,
        float positionScale,
        bool expectAnotherLod)
    {

        EnsureAvailable(cursor, 8, chunkEnd, "LOD header");
        float lodDistance = ReadSingle(data, cursor);
        if (!float.IsFinite(lodDistance) || lodDistance < 0)
        {
            throw new InvalidDataException("XBG LOD distance is invalid.");
        }

        int bufferCount = ReadPositiveCount(data, cursor + 4, 32, "vertex buffer");
        cursor += 8;
        EnsureAvailable(cursor, checked(bufferCount * 16), chunkEnd, "vertex-buffer descriptors");

        var buffers = new VertexBufferDescriptor[bufferCount];
        int totalVertexCount = 0;
        int totalVertexBytes = 0;
        for (int i = 0; i < bufferCount; i++)
        {
            uint flags = ReadUInt32(data, cursor);
            int stride = checked((int)ReadUInt32(data, cursor + 4));
            int vertexCount = checked((int)ReadUInt32(data, cursor + 8));
            uint declaredOffset = ReadUInt32(data, cursor + 12);
            cursor += 16;

            if (stride is < 8 or > 256 || vertexCount <= 0)
            {
                throw new InvalidDataException($"XBG vertex buffer {i} has invalid dimensions.");
            }

            int byteCount = checked(stride * vertexCount);
            totalVertexBytes = checked(totalVertexBytes + byteCount);
            totalVertexCount = checked(totalVertexCount + vertexCount);
            if (totalVertexCount > MaxVertexCount)
            {
                throw new InvalidDataException($"XBG LOD exceeds the {MaxVertexCount:N0}-vertex safety limit.");
            }

            buffers[i] = new(flags, stride, vertexCount, declaredOffset);
        }

        IReadOnlyList<SectionDescriptor> sectionDescriptors = ReadSectionDescriptors(data, cursor, chunkEnd);
        IndexBufferLocation indices = LocateBuffers(
            data,
            cursor,
            chunkEnd,
            totalVertexBytes,
            buffers,
            sectionDescriptors,
            sectionDescriptors.Count == 0 ? 3 : checked(sectionDescriptors.Max(section => section.IndexStart) + 3),
            expectAnotherLod);

        var positions = new Vector3[totalVertexCount];
        var normals = new Vector3[totalVertexCount];
        var textureCoordinates = new Vector2[totalVertexCount];
        int[] bufferVertexOffsets = new int[bufferCount];
        int vertexByteOffset = indices.VertexDataOffset;
        int vertexOffset = 0;
        for (int i = 0; i < buffers.Length; i++)
        {
            VertexBufferDescriptor buffer = buffers[i];
            bufferVertexOffsets[i] = vertexOffset;
            DecodeVertices(
                data,
                vertexByteOffset,
                buffer,
                positionScale,
                positions.AsSpan(vertexOffset, buffer.VertexCount),
                normals.AsSpan(vertexOffset, buffer.VertexCount),
                textureCoordinates.AsSpan(vertexOffset, buffer.VertexCount));
            vertexByteOffset += checked(buffer.Stride * buffer.VertexCount);
            vertexOffset += buffer.VertexCount;
        }

        ushort[] localIndices = ReadIndices(data, indices);
        XbgMeshSection[] sections = BuildSections(
            localIndices,
            sectionDescriptors,
            buffers,
            bufferVertexOffsets);
        FillMissingNormals(positions, normals, sections);

        var lod = new XbgMeshLod(
            lodDistance,
            Array.AsReadOnly(positions),
            Array.AsReadOnly(normals),
            Array.AsReadOnly(textureCoordinates),
            sections);
        return new(lod, indices.EndOffset);
    }

    private static IReadOnlyList<XbgMaterialReference> ReadMaterials(
        byte[] data,
        IReadOnlyList<XbgChunkInfo> chunks)
    {
        XbgChunkInfo? chunk = chunks.FirstOrDefault(candidate => candidate.Name == "LTMR");
        if (chunk is null)
        {
            return Array.Empty<XbgMaterialReference>();
        }

        int cursor = checked((int)chunk.Offset + ChunkHeaderSize);
        int end = checked((int)(chunk.Offset + chunk.ChunkSize));
        EnsureAvailable(cursor, 4, end, "LTMR material count");
        int count = checked((int)ReadUInt32(data, cursor));
        cursor += 4;
        if (count is < 0 or > 4096)
        {
            throw new InvalidDataException("XBG material count exceeds the safety limit.");
        }

        var materials = new XbgMaterialReference[count];
        for (int i = 0; i < count; i++)
        {
            string path = ReadLengthPrefixedString(data, ref cursor, end);
            string name = ReadLengthPrefixedString(data, ref cursor, end);
            materials[i] = new(name, path);
        }

        return Array.AsReadOnly(materials);
    }

    private static string ReadLengthPrefixedString(byte[] data, ref int cursor, int end)
    {
        EnsureAvailable(cursor, 4, end, "material string length");
        int length = checked((int)ReadUInt32(data, cursor));
        cursor += 4;
        if (length <= 0 || length > 1024 * 1024)
        {
            throw new InvalidDataException("XBG material string length is invalid.");
        }

        EnsureAvailable(cursor, checked(length + 1), end, "material string");
        string value = Encoding.UTF8.GetString(data, cursor, length);
        cursor += length;
        if (data[cursor++] != 0)
        {
            throw new InvalidDataException("XBG material string is not null-terminated.");
        }

        return value;
    }

    private static float ReadPositionScale(byte[] data, IReadOnlyList<XbgChunkInfo> chunks)
    {
        XbgChunkInfo? chunk = chunks.FirstOrDefault(candidate => candidate.Name == "PMCP");
        if (chunk is null || chunk.DataSize < 8)
        {
            return 1f;
        }

        int offset = checked((int)chunk.Offset + ChunkHeaderSize + 4);
        float storedScale = ReadSingle(data, offset);
        const float baseline = 1f / 16384f;
        return float.IsFinite(storedScale) && storedScale > 0 && MathF.Abs(storedScale - baseline) > 0.000001f
            ? storedScale * 16384f
            : 1f;
    }

    private static IReadOnlyList<SectionDescriptor> ReadSectionDescriptors(byte[] data, int offset, int chunkEnd)
    {
        EnsureAvailable(offset, 4, chunkEnd, "mesh section count");
        int count = checked((int)ReadUInt32(data, offset));
        if (count is <= 0 or > 65_536 || (long)offset + 4 + ((long)count * 20) > chunkEnd)
        {
            return Array.Empty<SectionDescriptor>();
        }

        var sections = new SectionDescriptor[count];
        int cursor = offset + 4;
        for (int i = 0; i < count; i++)
        {
            sections[i] = new(
                checked((int)ReadUInt32(data, cursor)),
                checked((int)ReadUInt32(data, cursor + 8)),
                checked((int)ReadUInt32(data, cursor + 12)));
            cursor += 20;
        }

        return Array.AsReadOnly(sections);
    }

    private static IndexBufferLocation LocateBuffers(
        byte[] data,
        int metadataOffset,
        int chunkEnd,
        int vertexByteCount,
        IReadOnlyList<VertexBufferDescriptor> buffers,
        IReadOnlyList<SectionDescriptor> sections,
        int minimumIndexCount,
        bool expectAnotherLod)
    {
        int lastCandidate = Math.Min(
            checked(metadataOffset + MaxVertexMetadataSize),
            checked(chunkEnd - vertexByteCount - 4));
        IndexBufferLocation? fallback = null;

        for (int vertexOffset = metadataOffset; vertexOffset <= lastCandidate; vertexOffset++)
        {
            int indexHeader = checked(vertexOffset + vertexByteCount);
            uint rawStoredCount = ReadUInt32(data, indexHeader);
            if (rawStoredCount == 0 || rawStoredCount > MaxIndexCount + 32)
            {
                continue;
            }

            int storedCount = checked((int)rawStoredCount);
            long minimumIndexEnd = (long)indexHeader + 4 + ((long)storedCount * 2);
            if (minimumIndexEnd > chunkEnd)
            {
                continue;
            }

            int indexDataOffset = indexHeader + 4;
            int paddingBytes = DetectIndexPadding(data, indexDataOffset, chunkEnd - indexDataOffset);
            int indexCount = storedCount;
            long indexEnd = minimumIndexEnd + paddingBytes;
            if (indexEnd > chunkEnd)
            {
                continue;
            }

            if (indexCount < minimumIndexCount || indexCount % 3 != 0 || indexCount > MaxIndexCount)
            {
                continue;
            }

            int actualOffset = indexDataOffset + paddingBytes;
            if (!IndicesArePlausible(data, actualOffset, indexCount, buffers, sections))
            {
                continue;
            }

            var location = new IndexBufferLocation(vertexOffset, actualOffset, indexCount, checked((int)indexEnd));
            if (!expectAnotherLod || FindPlausibleNextLod(data, location.EndOffset, chunkEnd) is not null)
            {
                return location;
            }

            fallback ??= location;
        }

        return fallback ?? throw new InvalidDataException("XBG vertex and index buffers could not be located safely.");
    }

    private static int DetectIndexPadding(byte[] data, int offset, int byteCount)
    {
        int runLength = data[offset];
        if (runLength is < 2 or > 32 || (runLength & 1) != 0 || runLength > byteCount)
        {
            return 0;
        }

        for (int i = 0; i < runLength; i++)
        {
            if (data[offset + i] != runLength - i)
            {
                return 0;
            }
        }

        return runLength;
    }

    private static bool IndicesArePlausible(
        byte[] data,
        int offset,
        int count,
        IReadOnlyList<VertexBufferDescriptor> buffers,
        IReadOnlyList<SectionDescriptor> sections)
    {
        List<SectionDescriptor> ordered = sections
            .Where(section => section.VertexBufferIndex >= 0 && section.VertexBufferIndex < buffers.Count)
            .GroupBy(section => section.IndexStart)
            .Select(group => group.First())
            .OrderBy(section => section.IndexStart)
            .ToList();
        if (ordered.Any(section => section.IndexStart < 0 || section.IndexStart >= count))
        {
            return false;
        }

        if (ordered.Count == 0 || ordered[0].IndexStart != 0)
        {
            ordered.Insert(0, new(0, 0, 0));
        }

        bool hasNonDegenerateTriangle = false;
        for (int sectionIndex = 0; sectionIndex < ordered.Count; sectionIndex++)
        {
            SectionDescriptor section = ordered[sectionIndex];
            int end = sectionIndex + 1 < ordered.Count ? ordered[sectionIndex + 1].IndexStart : count;
            if ((end - section.IndexStart) % 3 != 0)
            {
                return false;
            }

            int vertexCount = buffers[section.VertexBufferIndex].VertexCount;
            for (int i = section.IndexStart; i < end; i += 3)
            {
                ushort a = ReadUInt16(data, offset + (i * 2));
                ushort b = ReadUInt16(data, offset + ((i + 1) * 2));
                ushort c = ReadUInt16(data, offset + ((i + 2) * 2));
                if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                {
                    return false;
                }

                hasNonDegenerateTriangle |= a != b && b != c && a != c;
            }
        }

        return hasNonDegenerateTriangle;
    }

    private static int? FindPlausibleNextLod(byte[] data, int offset, int chunkEnd)
    {
        if (offset > chunkEnd - 24)
        {
            return null;
        }

        int end = Math.Min(checked(offset + 512), chunkEnd - 24);
        for (int cursor = offset; cursor <= end; cursor++)
        {
            float distance = ReadSingle(data, cursor);
            uint bufferCount = ReadUInt32(data, cursor + 4);
            uint stride = ReadUInt32(data, cursor + 12);
            uint vertexCount = ReadUInt32(data, cursor + 16);
            if (float.IsFinite(distance) && distance >= 0 && bufferCount is >= 1 and <= 32 &&
                stride is >= 8 and <= 256 && vertexCount is > 0 and <= MaxVertexCount)
            {
                return cursor;
            }
        }

        return null;
    }

    private static void DecodeVertices(
        byte[] data,
        int byteOffset,
        VertexBufferDescriptor buffer,
        float positionScale,
        Span<Vector3> positions,
        Span<Vector3> normals,
        Span<Vector2> textureCoordinates)
    {
        VertexLayout layout = GetVertexLayout(buffer.Flags, buffer.Stride);
        for (int i = 0; i < buffer.VertexCount; i++)
        {
            int offset = checked(byteOffset + (i * buffer.Stride));
            positions[i] = ReadPosition(data, offset + layout.PositionOffset, layout.PositionFlag) * positionScale;
            if (!float.IsFinite(positions[i].X) || !float.IsFinite(positions[i].Y) || !float.IsFinite(positions[i].Z))
            {
                throw new InvalidDataException("XBG contains a non-finite vertex position.");
            }

            normals[i] = layout.NormalOffset >= 0
                ? ReadPackedNormal(data, offset + layout.NormalOffset)
                : Vector3.Zero;
            textureCoordinates[i] = layout.TextureOffset >= 0
                ? ReadTextureCoordinate(data, offset + layout.TextureOffset)
                : Vector2.Zero;
        }
    }

    private static VertexLayout GetVertexLayout(uint flags, int stride)
    {
        int offset = 0;
        int positionOffset = -1;
        uint positionFlag = 0;
        int textureOffset = -1;
        int normalOffset = -1;
        bool positionFound = false;

        foreach ((uint flag, int size) in VertexComponents)
        {
            bool isPosition = flag is 0x0001 or 0x0002 or 0x0004;
            if ((flags & flag) == 0 || (isPosition && positionFound))
            {
                continue;
            }

            if (isPosition)
            {
                positionFound = true;
                positionOffset = offset;
                positionFlag = flag;
            }
            else if (flag == 0x0008)
            {
                textureOffset = offset;
            }
            else if (flag == 0x0040)
            {
                normalOffset = offset;
            }

            offset += size;
        }

        if (!positionFound || offset > stride)
        {
            throw new NotSupportedException($"Unsupported XBG vertex layout 0x{flags:X4} with stride {stride}.");
        }

        return new(positionOffset, positionFlag, textureOffset, normalOffset);
    }

    private static Vector3 ReadPosition(byte[] data, int offset, uint flag) => flag switch
    {
        0x0001 => new(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8)),
        0x0002 => new(
            ReadInt16(data, offset) / 16383.5f,
            ReadInt16(data, offset + 2) / 16383.5f,
            ReadInt16(data, offset + 4) / 16383.5f),
        0x0004 => new(
            (float)BitConverter.UInt16BitsToHalf(ReadUInt16(data, offset)),
            (float)BitConverter.UInt16BitsToHalf(ReadUInt16(data, offset + 2)),
            (float)BitConverter.UInt16BitsToHalf(ReadUInt16(data, offset + 4))),
        _ => throw new UnreachableException(),
    };

    private static Vector2 ReadTextureCoordinate(byte[] data, int offset) => new(
        (ReadInt16(data, offset) / 16383.5f) + 1f,
        2f - (ReadInt16(data, offset + 2) / 16383.5f));

    private static Vector3 ReadPackedNormal(byte[] data, int offset)
    {
        uint packed = ReadUInt32(data, offset);
        var normal = new Vector3(
            ((packed & 0x3FF) / 1023f * 2f) - 1f,
            (((packed >> 10) & 0x3FF) / 1023f * 2f) - 1f,
            (((packed >> 20) & 0x3FF) / 1023f * 2f) - 1f);
        return normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.Zero;
    }

    private static ushort[] ReadIndices(byte[] data, IndexBufferLocation location)
    {
        var indices = new ushort[location.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = ReadUInt16(data, location.DataOffset + (i * 2));
        }

        return indices;
    }

    private static XbgMeshSection[] BuildSections(
        ushort[] localIndices,
        IReadOnlyList<SectionDescriptor> descriptors,
        IReadOnlyList<VertexBufferDescriptor> buffers,
        int[] vertexOffsets)
    {
        IEnumerable<SectionDescriptor> candidates = descriptors
            .Where(section => section.VertexBufferIndex >= 0 &&
                              section.VertexBufferIndex < buffers.Count &&
                              section.IndexStart >= 0 &&
                              section.IndexStart < localIndices.Length)
            .GroupBy(section => section.IndexStart)
            .Select(group => group.First())
            .OrderBy(section => section.IndexStart);
        List<SectionDescriptor> ordered = candidates.ToList();
        if (ordered.Count == 0 || ordered[0].IndexStart != 0)
        {
            ordered.Insert(0, new(0, 0, 0));
        }

        var sections = new List<XbgMeshSection>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            SectionDescriptor descriptor = ordered[i];
            int end = i + 1 < ordered.Count ? ordered[i + 1].IndexStart : localIndices.Length;
            int count = end - descriptor.IndexStart;
            if (count <= 0 || count % 3 != 0)
            {
                throw new InvalidDataException("XBG mesh section does not contain complete triangles.");
            }

            VertexBufferDescriptor buffer = buffers[descriptor.VertexBufferIndex];
            int vertexOffset = vertexOffsets[descriptor.VertexBufferIndex];
            var indices = new int[count];
            for (int j = 0; j < count; j++)
            {
                int localIndex = localIndices[descriptor.IndexStart + j];
                if (localIndex >= buffer.VertexCount)
                {
                    throw new InvalidDataException("XBG triangle index exceeds its vertex buffer.");
                }

                indices[j] = checked(vertexOffset + localIndex);
            }

            sections.Add(new(descriptor.MaterialIndex, Array.AsReadOnly(indices)));
        }

        return sections.ToArray();
    }

    private static void FillMissingNormals(
        Vector3[] positions,
        Span<Vector3> normals,
        XbgMeshSection[] sections)
    {
        if (!normals.Contains(Vector3.Zero))
        {
            return;
        }

        var generated = new Vector3[normals.Length];
        foreach (XbgMeshSection section in sections)
        {
            IReadOnlyList<int> indices = section.TriangleIndices;
            for (int i = 0; i < indices.Count; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                generated[a] += face;
                generated[b] += face;
                generated[c] += face;
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i] == Vector3.Zero && generated[i].LengthSquared() > 0.000001f)
            {
                normals[i] = Vector3.Normalize(generated[i]);
            }
        }
    }

    private static int ReadPositiveCount(byte[] data, int offset, int limit, string label)
    {
        uint value = ReadUInt32(data, offset);
        if (value == 0 || value > limit)
        {
            throw new InvalidDataException($"XBG {label} count {value} is invalid.");
        }

        return checked((int)value);
    }

    private static void EnsureAvailable(int offset, int count, int end, string label)
    {
        if (offset < 0 || count < 0 || (long)offset + count > end)
        {
            throw new InvalidDataException($"XBG {label} exceeds the chunk bounds.");
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static short ReadInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static float ReadSingle(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));

    private sealed record VertexBufferDescriptor(uint Flags, int Stride, int VertexCount, uint DeclaredOffset);

    private sealed record SectionDescriptor(int VertexBufferIndex, int MaterialIndex, int IndexStart);

    private sealed record IndexBufferLocation(int VertexDataOffset, int DataOffset, int Count, int EndOffset);

    private sealed record DecodedLod(XbgMeshLod Lod, int EndOffset);

    private sealed record VertexLayout(int PositionOffset, uint PositionFlag, int TextureOffset, int NormalOffset);
}
