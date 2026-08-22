namespace Dunia.Formats.Fcb;

public static class FcbTypedValueProjector
{
    public static FcbTypedValueProjection Project(
        uint nodeTypeHash,
        FcbField field,
        FcbValueSchema schema)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(schema);

        if (!schema.TryResolve(nodeTypeHash, field.NameHash, out FcbValueKind codec))
        {
            return new(FcbTypedValueStatus.MissingSchema, null, null);
        }

        FcbValueCandidate? candidate = FcbValueProjector.Project(field).Candidates
            .SingleOrDefault(candidate => candidate.Kind == codec);
        return candidate is null
            ? new(FcbTypedValueStatus.Incompatible, codec, null)
            : new(FcbTypedValueStatus.Resolved, codec, candidate.Value);
    }
}
