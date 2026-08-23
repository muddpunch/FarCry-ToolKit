namespace Dunia.Formats.Meshes;

public sealed record XbgSummary(
    uint Version,
    uint ResourceHash,
    uint DeclaredSize,
    long FileSize,
    int MaterialCount,
    int LodCount,
    IReadOnlyList<XbgChunkInfo> Chunks);
