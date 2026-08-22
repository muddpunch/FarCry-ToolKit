namespace Dunia.Formats.Fcb;

public sealed record FcbNameDiscoveryMatch(
    uint Hash,
    string Name,
    long SourceOffset);
