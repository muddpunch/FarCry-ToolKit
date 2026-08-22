namespace Dunia.Formats.Fcb;

public static class FcbGraph
{
    public static IReadOnlyList<FcbNode> GetUniqueNodes(FcbDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = new List<FcbNode>();
        var seen = new HashSet<FcbNode>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<FcbNode>();
        pending.Enqueue(document.Root);
        while (pending.TryDequeue(out FcbNode? node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            result.Add(node);
            foreach (FcbNode child in node.Children)
            {
                pending.Enqueue(child);
            }
        }

        return result.AsReadOnly();
    }
}
