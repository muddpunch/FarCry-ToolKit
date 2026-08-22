namespace Dunia.Formats.Fcb;

public sealed record FcbRoundTripVerificationResult(
    long Length,
    string SourceSha256,
    string OutputSha256,
    bool IsByteExact);
