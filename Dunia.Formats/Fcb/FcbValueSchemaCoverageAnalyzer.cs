namespace Dunia.Formats.Fcb;

public static class FcbValueSchemaCoverageAnalyzer
{
    public static FcbValueSchemaCoverageReport Analyze(
        FcbDocument document,
        FcbValueSchema schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);

        int fields = 0;
        int resolved = 0;
        int missing = 0;
        int incompatible = 0;
        var missingKeys = new HashSet<FcbValueSchemaKey>();
        var incompatibleKeys = new HashSet<FcbValueSchemaKey>();

        foreach (FcbNode node in FcbGraph.GetUniqueNodes(document))
        {
            foreach (FcbField field in node.Fields)
            {
                fields++;
                FcbTypedValueProjection projection = FcbTypedValueProjector.Project(node.TypeHash, field, schema);
                var key = new FcbValueSchemaKey(node.TypeHash, field.NameHash);
                switch (projection.Status)
                {
                    case FcbTypedValueStatus.Resolved:
                        resolved++;
                        break;
                    case FcbTypedValueStatus.MissingSchema:
                        missing++;
                        missingKeys.Add(key);
                        break;
                    case FcbTypedValueStatus.Incompatible:
                        incompatible++;
                        incompatibleKeys.Add(key);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown typed projection status: {projection.Status}.");
                }
            }
        }

        return new(
            fields,
            resolved,
            missing,
            incompatible,
            Array.AsReadOnly(missingKeys.OrderBy(key => key.NodeTypeHash).ThenBy(key => key.FieldHash).ToArray()),
            Array.AsReadOnly(incompatibleKeys.OrderBy(key => key.NodeTypeHash).ThenBy(key => key.FieldHash).ToArray()));
    }
}
