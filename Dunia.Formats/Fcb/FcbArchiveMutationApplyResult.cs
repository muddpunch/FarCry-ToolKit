using Dunia.Formats.Archives;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveMutationApplyResult(
    ArchivePair TargetPair,
    ArchivePairBackupResult? Backup,
    int EntryIndex,
    ulong ResourceNameHash,
    FcbValueKind Codec,
    string SourcePayloadSha256,
    int PayloadLength,
    string PayloadSha256,
    int ArchiveEntryCount,
    bool NoOp,
    bool SemanticVerified);
