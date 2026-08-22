namespace Dunia.Formats.Fcb;

public sealed record FcbValueCandidate(
    FcbValueKind Kind,
    FcbValueEvidence Evidence,
    string Value);
