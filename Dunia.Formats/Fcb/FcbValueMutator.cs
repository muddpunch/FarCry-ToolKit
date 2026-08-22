namespace Dunia.Formats.Fcb;

public static class FcbValueMutator
{
    public static FcbValueMutationResult ReplaceInlineField(
        FcbDocument document,
        FcbField target,
        ReadOnlySpan<byte> replacement,
        FcbValueSchema schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(schema);

        FcbValueSchemaCoverageReport sourceCoverage = FcbValueSchemaCoverageAnalyzer.Analyze(document, schema);
        if (!sourceCoverage.IsComplete)
        {
            throw new InvalidOperationException("FCB mutation requires complete compatible schema coverage.");
        }

        (FcbNode owner, int matches) = FindOwner(document, target);
        if (matches != 1)
        {
            throw new ArgumentException("Target field must belong to the document exactly once.", nameof(target));
        }

        if (target.IsReference)
        {
            throw new NotSupportedException("Referenced FCB fields cannot be replaced independently.");
        }

        if (!schema.TryResolve(owner.TypeHash, target.NameHash, out FcbValueKind codec))
        {
            throw new InvalidOperationException("Target field has no schema codec.");
        }

        var candidate = new FcbField(target.NameHash, replacement.ToArray(), target.SourceOffset, null);
        if (FcbTypedValueProjector.Project(owner.TypeHash, candidate, schema).Status != FcbTypedValueStatus.Resolved)
        {
            throw new ArgumentException($"Replacement is incompatible with schema codec {codec}.", nameof(replacement));
        }

        FcbDocument mutated = CloneReplacing(document, target, candidate.Data.ToArray());
        byte[] serialized = Serialize(mutated);
        using var input = new MemoryStream(serialized, false);
        FcbDocument reparsed = FcbReader.Read(input);
        if (!FcbValueSchemaCoverageAnalyzer.Analyze(reparsed, schema).IsComplete)
        {
            throw new InvalidDataException("Serialized FCB no longer has complete schema coverage.");
        }

        byte[] verified = Serialize(reparsed);
        if (!serialized.AsSpan().SequenceEqual(verified))
        {
            throw new InvalidDataException("Modified FCB failed serialize/reparse verification.");
        }

        return new(reparsed, serialized, codec);
    }

    private static (FcbNode Owner, int Matches) FindOwner(FcbDocument document, FcbField target)
    {
        FcbNode? owner = null;
        int matches = 0;
        foreach (FcbNode node in FcbGraph.GetUniqueNodes(document))
        {
            foreach (FcbField field in node.Fields)
            {
                if (ReferenceEquals(field, target))
                {
                    owner = node;
                    matches++;
                }
            }
        }

        return (owner!, matches);
    }

    private static FcbDocument CloneReplacing(FcbDocument source, FcbField target, byte[] replacement)
    {
        IReadOnlyList<FcbNode> sourceNodes = FcbGraph.GetUniqueNodes(source);
        var nodes = new Dictionary<FcbNode, FcbNode>(ReferenceEqualityComparer.Instance);
        foreach (FcbNode node in sourceNodes)
        {
            nodes.Add(node, new FcbNode(node.SourceOffset) { TypeHash = node.TypeHash });
        }

        var fields = new Dictionary<FcbField, FcbField>(ReferenceEqualityComparer.Instance);

        foreach (FcbNode node in sourceNodes)
        {
            foreach (FcbField field in node.Fields.Where(field => !field.IsReference))
            {
                byte[] data = ReferenceEquals(field, target) ? replacement : field.Data.ToArray();
                fields.Add(field, new FcbField(field.NameHash, data, field.SourceOffset, null));
            }
        }

        foreach (FcbNode node in sourceNodes)
        {
            foreach (FcbField field in node.Fields.Where(field => field.IsReference))
            {
                FcbField referenceTarget = fields[field.ReferenceTarget!];
                fields.Add(field, new FcbField(field.NameHash, referenceTarget.Data.ToArray(), field.SourceOffset, referenceTarget));
            }
        }

        foreach (FcbNode node in sourceNodes)
        {
            FcbNode clone = nodes[node];
            clone.MutableFields.AddRange(node.Fields.Select(field => fields[field]));
            clone.MutableChildren.AddRange(node.Children.Select(child => nodes[child]));
        }

        return new(source.Header, nodes[source.Root], source.UniqueNodeCount, source.FieldCount);
    }

    private static byte[] Serialize(FcbDocument document)
    {
        using var output = new MemoryStream();
        FcbWriter.Write(output, document);
        return output.ToArray();
    }
}
