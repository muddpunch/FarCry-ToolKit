namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10DirectoryPackItem(
    string ResourcePath,
    ulong ResourceHash,
    IReadOnlyList<int> EntryIndices,
    long Length,
    string Sha256);
