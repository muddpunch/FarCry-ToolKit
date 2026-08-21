namespace Dunia.Formats.Changes;

public sealed record PendingChangeEntry<TKey, TChange>(TKey Key, TChange Change)
    where TKey : notnull
    where TChange : notnull;

