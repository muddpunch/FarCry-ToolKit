namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchiveNameDiscoveryMatch(
    int SourceEntryIndex,
    ulong Hash,
    string Name);
