namespace Dunia.Formats.Textures;

public sealed record XbtArchiveTransactionPlan(
    IReadOnlyList<XbtArchiveReplacementPlan> Replacements,
    string PlanSha256);
