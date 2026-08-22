namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMutationManifestResult(
    int EntryIndex,
    ulong ResourceNameHash,
    int ArchiveEntryCount,
    int SchemaFieldCount,
    int SchemaResolvedCount,
    int SourcePayloadLength,
    string SourcePayloadSha256,
    IReadOnlyList<FcbArchiveMutationManifestEntry> Entries,
    int ReferencedFieldCount);
