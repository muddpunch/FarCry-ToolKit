using Dunia.Formats.Archives;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveBatchMutationApplyResult(
    ArchivePair TargetPair,
    ArchivePairBackupResult? Backup,
    FcbArchiveBatchMutationPlanResult Plan,
    bool NoOp,
    bool SemanticVerified);
