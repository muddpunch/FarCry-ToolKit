namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ReplacementVerificationResult(
    int EntryIndex,
    ulong NameHash,
    string SourcePayloadSha256,
    string RebuiltPayloadSha256,
    bool UnchangedEntriesExact,
    bool OriginalDataPrefixExact,
    FatV10CompressionScheme SourceCompression,
    long RebuiltOffset)
{
    public bool IsValid =>
        string.Equals(SourcePayloadSha256, RebuiltPayloadSha256, StringComparison.Ordinal) &&
        UnchangedEntriesExact &&
        OriginalDataPrefixExact;
}
