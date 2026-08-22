namespace Dunia.Formats.Hashing;

public static class DuniaNameCoverageAnalyzer
{
    public static DuniaNameCoverageReport Analyze(
        IEnumerable<ulong> hashes,
        DuniaNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(resolver);

        int entries = 0;
        int resolved = 0;
        int unknown = 0;
        int collisions = 0;
        var unknownHashes = new HashSet<ulong>();
        var collisionHashes = new HashSet<ulong>();

        foreach (ulong hash in hashes)
        {
            entries++;
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
            entries,
            resolved,
            unknown,
            collisions,
            Array.AsReadOnly(unknownHashes.Order().ToArray()),
            Array.AsReadOnly(collisionHashes.Order().ToArray()));
    }
}
