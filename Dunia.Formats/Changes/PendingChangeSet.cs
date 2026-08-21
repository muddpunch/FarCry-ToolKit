namespace Dunia.Formats.Changes;

public sealed class PendingChangeSet<TKey, TChange>
    where TKey : notnull
    where TChange : notnull
{
    private readonly Dictionary<TKey, TChange> changes;
    private readonly List<TKey> order = [];
    private readonly object syncRoot = new();
    private long version;

    public PendingChangeSet(IEqualityComparer<TKey>? comparer = null) =>
        changes = new(comparer);

    public int Count
    {
        get
        {
            lock (syncRoot)
            {
                return changes.Count;
            }
        }
    }

    public PendingChangeDisposition Stage(TKey key, TChange change)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(change);

        lock (syncRoot)
        {
            bool exists = changes.ContainsKey(key);
            changes[key] = change;

            if (!exists)
            {
                order.Add(key);
            }

            version++;
            return exists
                ? PendingChangeDisposition.Replaced
                : PendingChangeDisposition.Added;
        }
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (syncRoot)
        {
            if (!changes.Remove(key))
            {
                return false;
            }

            for (int index = 0; index < order.Count; index++)
            {
                if (!changes.Comparer.Equals(order[index], key))
                {
                    continue;
                }

                order.RemoveAt(index);
                break;
            }

            version++;
            return true;
        }
    }

    public int Discard()
    {
        lock (syncRoot)
        {
            int count = changes.Count;
            if (count == 0)
            {
                return 0;
            }

            changes.Clear();
            order.Clear();
            version++;
            return count;
        }
    }

    public PendingChangeSnapshot<TKey, TChange> Snapshot()
    {
        lock (syncRoot)
        {
            var items = new PendingChangeEntry<TKey, TChange>[order.Count];

            for (int index = 0; index < order.Count; index++)
            {
                TKey key = order[index];
                items[index] = new(key, changes[key]);
            }

            return new(version, Array.AsReadOnly(items));
        }
    }
}
