namespace Dunia.Formats.Fcb;

public sealed record FcbValueSchemaCoverageReport(
    int FieldCount,
    int ResolvedCount,
    int MissingCount,
    int IncompatibleCount,
    IReadOnlyList<FcbValueSchemaKey> MissingKeys,
    IReadOnlyList<FcbValueSchemaKey> IncompatibleKeys)
{
    public bool IsComplete => MissingCount == 0 && IncompatibleCount == 0;
}
