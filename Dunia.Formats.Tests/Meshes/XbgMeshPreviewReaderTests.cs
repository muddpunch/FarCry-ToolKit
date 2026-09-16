using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Dunia.Formats.Meshes;

namespace Dunia.Formats.Tests.Meshes;

public sealed class XbgMeshPreviewReaderTests
{
    [Fact]
    public void ReadDecodesHighestDetailGeometryAndMaterials()
    {
        byte[] data = BuildMesh(invalidIndex: false);
        using var input = new MemoryStream(data, writable: false);

        XbgMeshPreview mesh = XbgMeshPreviewReader.Read(input);

        Assert.Equal(4, mesh.Positions.Count);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal([0, 1, 2, 0, 2, 3], mesh.Sections.Single().TriangleIndices);
        Assert.Equal("Preview", mesh.Materials.Single().Name);
        Assert.Equal("graphics\\preview.material.bin", mesh.Materials.Single().Path);
        Assert.InRange(MathF.Abs(mesh.Normals[0].X), 0, 0.002f);
        Assert.InRange(MathF.Abs(mesh.Normals[0].Y), 0, 0.002f);
        Assert.InRange(mesh.Normals[0].Z, 0.999f, 1f);
    }

    [Fact]
    public void ReadRejectsAnIndexOutsideItsVertexBuffer()
    {
        byte[] data = BuildMesh(invalidIndex: true);
        using var input = new MemoryStream(data, writable: false);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => XbgMeshPreviewReader.Read(input));

        Assert.Contains("buffers could not be located", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDecodesEveryLodAndSkipsIndexAlignmentPadding()
    {
        byte[] data = BuildMesh(invalidIndex: false, lodCount: 2, indexPaddingBytes: 12);
        using var input = new MemoryStream(data, writable: false);

        XbgMeshPreview mesh = XbgMeshPreviewReader.Read(input);

        Assert.Equal(2, mesh.Lods.Count);
        Assert.All(mesh.Lods, lod => Assert.Equal(2, lod.TriangleCount));
        Assert.Equal(10f, mesh.Lods[0].Distance);
        Assert.Equal(20f, mesh.Lods[1].Distance);
    }

    [Fact]
    public void ExportFbxWritesAllLodsGeometryUvNormalsAndMaterialBindings()
    {
        using var input = new MemoryStream(BuildMesh(invalidIndex: false, lodCount: 2), writable: false);
        XbgMeshPreview mesh = XbgMeshPreviewReader.Read(input);
        using var output = new MemoryStream();

        XbgFbxExporter.Export(mesh, output);

        string fbx = Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("; FBX 7.4.0 project file\n", fbx, StringComparison.Ordinal);
        Assert.Equal(2, Count(fbx, "\tGeometry: "));
        Assert.Contains("Model::LOD0_Section0", fbx, StringComparison.Ordinal);
        Assert.Contains("Model::LOD1_Section0", fbx, StringComparison.Ordinal);
        Assert.Contains("PolygonVertexIndex: *6", fbx, StringComparison.Ordinal);
        Assert.Contains("LayerElementNormal", fbx, StringComparison.Ordinal);
        Assert.Contains("LayerElementUV", fbx, StringComparison.Ordinal);
        Assert.Contains("Material::Preview", fbx, StringComparison.Ordinal);
        Assert.Contains("graphics\\\\preview.material.bin", fbx, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportFbxRejectsAnInvalidSectionBeforeWriting()
    {
        var lod = new XbgMeshLod(
            0,
            [Vector3.Zero],
            [Vector3.UnitZ],
            [Vector2.Zero],
            [new XbgMeshSection(0, [0, 1, 0])]);
        var mesh = new XbgMeshPreview(null!, [], [lod]);
        using var output = new MemoryStream();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => XbgFbxExporter.Export(mesh, output));

        Assert.Contains("triangle indices", exception.Message, StringComparison.Ordinal);
        Assert.Empty(output.ToArray());
    }

    private static int Count(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static byte[] BuildMesh(bool invalidIndex, int lodCount = 1, int indexPaddingBytes = 0)
    {
        byte[] material = BuildMaterialChunk();
        byte[] geometry = BuildGeometryChunk(invalidIndex, lodCount, indexPaddingBytes);
        byte[] positionScale = BuildPositionScaleChunk();
        byte[] data = new byte[32 + material.Length + geometry.Length + positionScale.Length];
        "HSEM"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), XbgSummaryReader.FarCry5Version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), checked((uint)data.Length - 12));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 3);
        material.CopyTo(data, 32);
        geometry.CopyTo(data, 32 + material.Length);
        positionScale.CopyTo(data, 32 + material.Length + geometry.Length);
        return data;
    }

    private static byte[] BuildMaterialChunk()
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            WriteExtendedString(writer, "graphics\\preview.material.bin");
            WriteExtendedString(writer, "Preview");
        }

        return BuildChunk("LTMR", payload.ToArray());
    }

    private static byte[] BuildGeometryChunk(bool invalidIndex, int lodCount, int indexPaddingBytes)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(lodCount);
            writer.Write(lodCount); // Aggregate vertex-buffer count.
            for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
            {
                writer.Write((lodIndex + 1) * 10f);
                writer.Write(1); // LOD vertex-buffer count.
                writer.Write(0x0042); // Quantized position + packed normal.
                writer.Write(12); // Stride.
                writer.Write(4); // Vertex count.
                writer.Write(0); // Vertex-buffer offset.
                writer.Write(1); // Section count.
                writer.Write(0); // Vertex-buffer index.
                writer.Write(0);
                writer.Write(0); // Material index.
                writer.Write(0); // First index.
                writer.Write(3); // Last vertex.
                writer.Write(new byte[4]); // Variable metadata/padding.

                WriteVertex(writer, -8192, -8192, 0);
                WriteVertex(writer, 8192, -8192, 0);
                WriteVertex(writer, 8192, 8192, 0);
                WriteVertex(writer, -8192, 8192, 0);

                writer.Write(6); // Index count excludes alignment padding.
                for (int padding = indexPaddingBytes; padding > 0; padding--)
                {
                    writer.Write((byte)padding);
                }

                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)2);
                writer.Write((ushort)0);
                writer.Write((ushort)2);
                writer.Write((ushort)(invalidIndex ? 7 : 3));
                if (lodIndex + 1 < lodCount)
                {
                    writer.Write(new byte[7]); // Variable inter-LOD trailer.
                }
            }
        }

        return BuildChunk("SDOL", payload.ToArray());
    }

    private static byte[] BuildPositionScaleChunk()
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), BitConverter.SingleToInt32Bits(1f / 16384f));
        return BuildChunk("PMCP", payload);
    }

    private static void WriteVertex(BinaryWriter writer, short x, short y, short z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        writer.Write((short)1);
        const uint unitZ = 512 | (512 << 10) | (1023 << 20);
        writer.Write(unitZ);
    }

    private static void WriteExtendedString(BinaryWriter writer, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        writer.Write(encoded.Length);
        writer.Write(encoded);
        writer.Write((byte)0);
    }

    private static byte[] BuildChunk(string name, byte[] payload)
    {
        byte[] chunk = new byte[20 + payload.Length];
        Encoding.ASCII.GetBytes(name).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), checked((uint)chunk.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), checked((uint)payload.Length));
        payload.CopyTo(chunk, 20);
        return chunk;
    }
}
