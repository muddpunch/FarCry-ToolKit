using Dunia.Formats.Archives;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveTransactionApplyResult(
    ArchivePair TargetPair,
    ArchivePairBackupResult? Backup,
    FcbArchiveTransactionPlanResult Plan,
    bool NoOp,
    bool SemanticVerified);
