using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

internal sealed record FcbArchiveBatchMutationArtifact(
    FatV10Index SourceIndex,
    FatV10Entry SourceEntry,
    long SourceDataLength,
    byte[] SourcePayload,
    byte[] PlannedPayload,
    FcbArchiveBatchMutationPlanResult Plan);
