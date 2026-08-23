namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveTransactionEntry(
    int EntryIndex,
    ulong ExpectedResourceNameHash,
    IReadOnlyList<FcbArchiveFieldMutation> Mutations);
