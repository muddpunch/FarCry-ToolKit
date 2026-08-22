namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchiveRoundTripVerificationResult(
    string SourceFatSha256,
    string OutputFatSha256,
    string SourceDatSha256,
    string OutputDatSha256,
    long FatLength,
    long DatLength)
{
    public bool IsByteExact =>
        string.Equals(SourceFatSha256, OutputFatSha256, StringComparison.Ordinal) &&
        string.Equals(SourceDatSha256, OutputDatSha256, StringComparison.Ordinal);
}
