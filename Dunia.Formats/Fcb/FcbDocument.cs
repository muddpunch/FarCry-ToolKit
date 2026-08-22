namespace Dunia.Formats.Fcb;

public sealed record FcbDocument(
    FcbHeader Header,
    FcbNode Root,
    int UniqueNodeCount,
    int FieldCount);
