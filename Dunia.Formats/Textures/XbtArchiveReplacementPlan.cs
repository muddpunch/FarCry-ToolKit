namespace Dunia.Formats.Textures;

public sealed record XbtArchiveReplacementPlan(
    int EntryIndex,
    ulong ResourceNameHash,
    string SourcePayloadSha256,
    string ReplacementPayloadSha256,
    long ReplacementLength,
    string PlanSha256);
