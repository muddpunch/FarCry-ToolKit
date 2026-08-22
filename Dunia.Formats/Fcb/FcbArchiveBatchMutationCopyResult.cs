using Dunia.Formats.Archives;

namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveBatchMutationCopyResult(
    ArchivePair OutputPair,
    FcbArchiveBatchMutationPlanResult Plan,
    bool PayloadExact);
