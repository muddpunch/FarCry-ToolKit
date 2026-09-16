using System.Globalization;
using System.Numerics;
using System.Text;

namespace Dunia.Formats.Meshes;

public static class XbgFbxExporter
{
    private const long RootId = 1;

    public static void Export(XbgMeshPreview mesh, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("FBX output must be writable.", nameof(output));
        }

        Validate(mesh);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };

        WriteHeader(writer, mesh);
        WriteObjects(writer, mesh);
        WriteConnections(writer, mesh);
        writer.Flush();
    }

    private static void Validate(XbgMeshPreview mesh)
    {
        if (mesh.Lods.Count == 0)
        {
            throw new InvalidDataException("XBG contains no exportable LODs.");
        }

        foreach (XbgMeshLod lod in mesh.Lods)
        {
            if (lod.Positions.Count == 0 || lod.Normals.Count != lod.Positions.Count ||
                lod.TextureCoordinates.Count != lod.Positions.Count)
            {
                throw new InvalidDataException("XBG vertex attribute counts are inconsistent.");
            }

            if (lod.Positions.Any(value => !IsFinite(value)) ||
                lod.Normals.Any(value => !IsFinite(value)) ||
                lod.TextureCoordinates.Any(value => !IsFinite(value)))
            {
                throw new InvalidDataException("XBG contains non-finite vertex attributes.");
            }

            foreach (XbgMeshSection section in lod.Sections)
            {
                if (section.TriangleIndices.Count == 0 || section.TriangleIndices.Count % 3 != 0 ||
                    section.TriangleIndices.Any(index => index < 0 || index >= lod.Positions.Count))
                {
                    throw new InvalidDataException("XBG contains invalid triangle indices.");
                }
            }
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static void WriteHeader(StreamWriter writer, XbgMeshPreview mesh)
    {
        int geometryCount = mesh.Lods.Sum(lod => lod.Sections.Count);
        int objectCount = 1 + geometryCount * 2 + mesh.Materials.Count;
        writer.WriteLine("; FBX 7.4.0 project file");
        writer.WriteLine("FBXHeaderExtension:  {");
        writer.WriteLine("\tFBXHeaderVersion: 1003");
        writer.WriteLine("\tFBXVersion: 7400");
        writer.WriteLine("}");
        writer.WriteLine("GlobalSettings:  {");
        writer.WriteLine("\tVersion: 1000");
        writer.WriteLine("\tProperties70:  {");
        writer.WriteLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",2");
        writer.WriteLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
        writer.WriteLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",1");
        writer.WriteLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",-1");
        writer.WriteLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
        writer.WriteLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
        writer.WriteLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
        writer.WriteLine("\t}");
        writer.WriteLine("}");
        writer.WriteLine("Definitions:  {");
        writer.WriteLine($"\tCount: {objectCount.ToString(CultureInfo.InvariantCulture)}");
        WriteDefinition(writer, "Model", 1 + geometryCount);
        WriteDefinition(writer, "Geometry", geometryCount);
        WriteDefinition(writer, "Material", mesh.Materials.Count);
        writer.WriteLine("}");
    }

    private static void WriteDefinition(StreamWriter writer, string type, int count)
    {
        writer.WriteLine($"\tObjectType: \"{type}\" {{");
        writer.WriteLine($"\t\tCount: {count.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine("\t}");
    }

    private static void WriteObjects(StreamWriter writer, XbgMeshPreview mesh)
    {
        writer.WriteLine("Objects:  {");
        writer.WriteLine($"\tModel: {RootId}, \"Model::Dunia_XBG\", \"Null\" {{");
        writer.WriteLine("\t\tVersion: 232");
        writer.WriteLine("\t}");

        for (int materialIndex = 0; materialIndex < mesh.Materials.Count; materialIndex++)
        {
            long id = MaterialId(mesh, materialIndex);
            XbgMaterialReference material = mesh.Materials[materialIndex];
            writer.WriteLine($"\tMaterial: {id}, \"Material::{Escape(NameOrFallback(material.Name, materialIndex))}\", \"\" {{");
            writer.WriteLine("\t\tVersion: 102");
            writer.WriteLine("\t\tShadingModel: \"phong\"");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine($"\t\t\tP: \"DuniaPath\", \"KString\", \"\", \"\",\"{Escape(material.Path)}\"");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t}");
        }

        int sectionOrdinal = 0;
        for (int lodIndex = 0; lodIndex < mesh.Lods.Count; lodIndex++)
        {
            XbgMeshLod lod = mesh.Lods[lodIndex];
            for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++, sectionOrdinal++)
            {
                XbgMeshSection section = lod.Sections[sectionIndex];
                long geometryId = GeometryId(sectionOrdinal);
                long modelId = ModelId(sectionOrdinal);
                string name = $"LOD{lodIndex}_Section{sectionIndex}";
                WriteGeometry(writer, geometryId, name, lod, section);
                writer.WriteLine($"\tModel: {modelId}, \"Model::{name}\", \"Mesh\" {{");
                writer.WriteLine("\t\tVersion: 232");
                writer.WriteLine("\t\tProperties70:  {");
                writer.WriteLine($"\t\t\tP: \"DuniaLodDistance\", \"double\", \"Number\", \"\",{Format(lod.Distance)}");
                writer.WriteLine($"\t\t\tP: \"DuniaMaterialIndex\", \"int\", \"Integer\", \"\",{section.MaterialIndex.ToString(CultureInfo.InvariantCulture)}");
                writer.WriteLine("\t\t}");
                writer.WriteLine("\t}");
            }
        }

        writer.WriteLine("}");
    }

    private static void WriteGeometry(
        StreamWriter writer,
        long id,
        string name,
        XbgMeshLod lod,
        XbgMeshSection section)
    {
        writer.WriteLine($"\tGeometry: {id}, \"Geometry::{name}\", \"Mesh\" {{");
        WriteVector3Array(writer, "Vertices", lod.Positions);
        writer.Write($"\t\tPolygonVertexIndex: *{section.TriangleIndices.Count.ToString(CultureInfo.InvariantCulture)} {{\n\t\t\ta: ");
        for (int i = 0; i < section.TriangleIndices.Count; i++)
        {
            int index = section.TriangleIndices[i];
            WriteSeparator(writer, i);
            writer.Write((i + 1) % 3 == 0 ? (-index - 1).ToString(CultureInfo.InvariantCulture) : index.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteLine("\n\t\t}");
        writer.WriteLine("\t\tLayerElementNormal: 0 {");
        writer.WriteLine("\t\t\tVersion: 101");
        writer.WriteLine("\t\t\tName: \"Normals\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
        WriteVector3Array(writer, "Normals", lod.Normals, 3);
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t\tLayerElementUV: 0 {");
        writer.WriteLine("\t\t\tVersion: 101");
        writer.WriteLine("\t\t\tName: \"UVChannel_1\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
        WriteVector2Array(writer, "UV", lod.TextureCoordinates, 3);
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t\tLayer: 0 {");
        writer.WriteLine("\t\t\tVersion: 100");
        WriteLayerElement(writer, "LayerElementNormal");
        WriteLayerElement(writer, "LayerElementUV");
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t}");
    }

    private static void WriteLayerElement(StreamWriter writer, string type)
    {
        writer.WriteLine("\t\t\tLayerElement:  {");
        writer.WriteLine($"\t\t\t\tType: \"{type}\"");
        writer.WriteLine("\t\t\t\tTypedIndex: 0");
        writer.WriteLine("\t\t\t}");
    }

    private static void WriteVector3Array(
        StreamWriter writer,
        string name,
        IReadOnlyList<Vector3> values,
        int indent = 2)
    {
        string tabs = new('\t', indent);
        writer.WriteLine($"{tabs}{name}: *{(values.Count * 3).ToString(CultureInfo.InvariantCulture)} {{");
        writer.Write($"{tabs}\ta: ");
        for (int i = 0; i < values.Count; i++)
        {
            Vector3 value = values[i];
            WriteSeparator(writer, i);
            writer.Write($"{Format(value.X)},{Format(value.Y)},{Format(value.Z)}");
        }

        writer.WriteLine($"\n{tabs}}}");
    }

    private static void WriteVector2Array(
        StreamWriter writer,
        string name,
        IReadOnlyList<Vector2> values,
        int indent)
    {
        string tabs = new('\t', indent);
        writer.WriteLine($"{tabs}{name}: *{(values.Count * 2).ToString(CultureInfo.InvariantCulture)} {{");
        writer.Write($"{tabs}\ta: ");
        for (int i = 0; i < values.Count; i++)
        {
            Vector2 value = values[i];
            WriteSeparator(writer, i);
            writer.Write($"{Format(value.X)},{Format(1f - value.Y)}");
        }

        writer.WriteLine($"\n{tabs}}}");
    }

    private static void WriteConnections(StreamWriter writer, XbgMeshPreview mesh)
    {
        writer.WriteLine("Connections:  {");
        writer.WriteLine($"\tC: \"OO\",{RootId},0");
        int sectionOrdinal = 0;
        foreach (XbgMeshLod lod in mesh.Lods)
        {
            foreach (XbgMeshSection section in lod.Sections)
            {
                long modelId = ModelId(sectionOrdinal);
                writer.WriteLine($"\tC: \"OO\",{GeometryId(sectionOrdinal)},{modelId}");
                writer.WriteLine($"\tC: \"OO\",{modelId},{RootId}");
                if (section.MaterialIndex >= 0 && section.MaterialIndex < mesh.Materials.Count)
                {
                    writer.WriteLine($"\tC: \"OO\",{MaterialId(mesh, section.MaterialIndex)},{modelId}");
                }

                sectionOrdinal++;
            }
        }

        writer.WriteLine("}");
    }

    private static long GeometryId(int ordinal) => 10_000L + ordinal * 2L;

    private static long ModelId(int ordinal) => GeometryId(ordinal) + 1;

    private static long MaterialId(XbgMeshPreview mesh, int index) =>
        20_000L + mesh.Lods.Sum(lod => lod.Sections.Count) * 2L + index;

    private static string NameOrFallback(string value, int index) =>
        string.IsNullOrWhiteSpace(value) ? $"Material_{index}" : value;

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void WriteSeparator(StreamWriter writer, int index)
    {
        if (index > 0)
        {
            writer.Write(',');
        }
    }
}
