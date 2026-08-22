namespace Dunia.Formats.Fcb;

public static class FcbNameCoverageAnalyzer
{
    public static FcbNameCoverageReport Analyze(
        IEnumerable<uint> hashes,
        FcbNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(resolver);

        int occurrences = 0;
        int resolved = 0;
        int unknown = 0;
        int collisions = 0;
        var unknownHashes = new HashSet<uint>();
        var collisionHashes = new HashSet<uint>();

        foreach (uint hash in hashes)
        {
            occurrences++;
            switch (resolver.Resolve(hash).Count)
            {
                case 0:
                    unknown++;
                    unknownHashes.Add(hash);
                    break;
                case 1:
                    resolved++;
                    break;
                default:
                    collisions++;
                    collisionHashes.Add(hash);
                    break;
            }
        }

        return new(
            occurrences,
            resolved,
            unknown,
            collisions,
            Array.AsReadOnly(unknownHashes.Order().ToArray()),
            Array.AsReadOnly(collisionHashes.Order().ToArray()));
    }
}
