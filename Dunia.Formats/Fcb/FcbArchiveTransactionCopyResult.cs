using Dunia.Formats.Archives;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveTransactionCopyResult(
    ArchivePair OutputPair,
    FcbArchiveTransactionPlanResult Plan,
    bool PayloadsExact);
