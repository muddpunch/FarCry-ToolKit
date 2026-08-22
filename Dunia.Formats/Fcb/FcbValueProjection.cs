namespace Dunia.Formats.Fcb;

public sealed record FcbValueProjection(
    string RawHex,
    IReadOnlyList<FcbValueCandidate> Candidates)
{
    public bool IsAmbiguous => Candidates.Count > 1;
}
