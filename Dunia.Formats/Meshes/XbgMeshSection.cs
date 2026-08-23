namespace Dunia.Formats.Meshes;

public sealed record XbgMeshSection(int MaterialIndex, IReadOnlyList<int> TriangleIndices);
