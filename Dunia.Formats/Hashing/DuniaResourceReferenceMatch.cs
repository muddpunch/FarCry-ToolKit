namespace Dunia.Formats.Hashing;

public sealed record DuniaResourceReferenceMatch(
    long Offset,
    ulong ResourceHash,
    DuniaResourceReferenceEndianness Endianness);
