namespace Dunia.Formats.Fcb;

public sealed record FcbHeader(
    ushort Version,
    ushort Flags,
    uint DeclaredObjectCount,
    uint DeclaredValueCount);
