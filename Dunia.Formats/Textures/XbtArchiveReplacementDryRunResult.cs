using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Textures;

public sealed record XbtArchiveReplacementDryRunResult(
    XbtArchiveReplacementPlan Plan,
    FatV10ArchivePatchBuildResult Build,
    bool Verified);
