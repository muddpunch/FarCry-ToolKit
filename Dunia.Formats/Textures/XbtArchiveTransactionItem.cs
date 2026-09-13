using Dunia.Formats.Changes;

namespace Dunia.Formats.Textures;

public sealed record XbtArchiveTransactionItem(
    int EntryIndex,
    ulong ResourceNameHash,
    string ExpectedSourcePayloadSha256,
    string ExpectedReplacementPayloadSha256,
    StagedReplacement Replacement);
