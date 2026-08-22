namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMutationManifestEntry(
    int NodeIndex,
    int FieldIndex,
    uint TypeHash,
    uint FieldHash,
    FcbValueKind Codec,
    string Value);
