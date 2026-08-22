namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveFieldMutation(
    int NodeIndex,
    int FieldIndex,
    uint ExpectedTypeHash,
    uint ExpectedFieldHash,
    string Value);
