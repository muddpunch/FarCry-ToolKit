namespace Dunia.Formats.Meshes;

public sealed record XbgMeshPreview(
    XbgSummary Summary,
    IReadOnlyList<XbgMaterialReference> Materials,
    IReadOnlyList<XbgMeshLod> Lods)
{
    public XbgMeshLod HighestDetailLod => Lods[0];

    public float LodDistance => HighestDetailLod.Distance;

    public IReadOnlyList<System.Numerics.Vector3> Positions => HighestDetailLod.Positions;

    public IReadOnlyList<System.Numerics.Vector3> Normals => HighestDetailLod.Normals;

    public IReadOnlyList<System.Numerics.Vector2> TextureCoordinates => HighestDetailLod.TextureCoordinates;

    public IReadOnlyList<XbgMeshSection> Sections => HighestDetailLod.Sections;

    public int TriangleCount => HighestDetailLod.TriangleCount;
}
