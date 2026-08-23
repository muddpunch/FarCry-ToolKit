using System.Numerics;

namespace Dunia.Formats.Meshes;

public sealed record XbgMeshLod(
    float Distance,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<Vector2> TextureCoordinates,
    IReadOnlyList<XbgMeshSection> Sections)
{
    public int TriangleCount => Sections.Sum(section => section.TriangleIndices.Count / 3);
}
