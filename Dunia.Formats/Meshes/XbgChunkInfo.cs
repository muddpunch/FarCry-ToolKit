namespace Dunia.Formats.Meshes;

public sealed record XbgChunkInfo(
    string Name,
    long Offset,
    uint Version,
    uint ChunkSize,
    uint DataSize);
