namespace Dunia.Formats.Changes;

public sealed record PendingChangeSnapshot<TKey, TChange>(
    long Version,
    IReadOnlyList<PendingChangeEntry<TKey, TChange>> Items)
    where TKey : notnull
    where TChange : notnull;

