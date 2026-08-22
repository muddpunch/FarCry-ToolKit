namespace Dunia.Formats.Archives;

internal sealed record ArchivePairFingerprint(
    long FatLength,
    string FatSha256,
    long DatLength,
    string DatSha256);
