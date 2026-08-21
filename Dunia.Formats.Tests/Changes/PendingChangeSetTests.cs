using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Changes;

public sealed class PendingChangeSetTests
{
    [Fact]
    public void StageReplacesSameTargetWithoutChangingOrder()
    {
        var changes = new PendingChangeSet<string, string>(StringComparer.OrdinalIgnoreCase);

        PendingChangeDisposition first = changes.Stage("first", "v1");
        changes.Stage("second", "v2");
        PendingChangeDisposition replacement = changes.Stage("FIRST", "v3");
        PendingChangeSnapshot<string, string> snapshot = changes.Snapshot();

        Assert.Equal(PendingChangeDisposition.Added, first);
        Assert.Equal(PendingChangeDisposition.Replaced, replacement);
        Assert.Equal(2, changes.Count);
        Assert.Collection(
            snapshot.Items,
            item =>
            {
                Assert.Equal("first", item.Key);
                Assert.Equal("v3", item.Change);
            },
            item => Assert.Equal("second", item.Key));
    }

    [Fact]
    public void RemoveOnlyAdvancesVersionWhenTargetExists()
    {
        var changes = new PendingChangeSet<string, string>(StringComparer.OrdinalIgnoreCase);
        changes.Stage("target", "one");
        long stagedVersion = changes.Snapshot().Version;

        bool missing = changes.Remove("missing");
        bool removed = changes.Remove("TARGET");
        PendingChangeSnapshot<string, string> snapshot = changes.Snapshot();

        Assert.False(missing);
        Assert.True(removed);
        Assert.Equal(stagedVersion + 1, snapshot.Version);
        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public void DiscardIsAtomicAndIdempotent()
    {
        var changes = new PendingChangeSet<int, string>();
        changes.Stage(1, "one");
        changes.Stage(2, "two");

        int discarded = changes.Discard();
        long version = changes.Snapshot().Version;
        int discardedAgain = changes.Discard();

        Assert.Equal(2, discarded);
        Assert.Equal(0, discardedAgain);
        Assert.Equal(version, changes.Snapshot().Version);
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void SnapshotDoesNotChangeAfterLaterMutations()
    {
        var changes = new PendingChangeSet<int, string>();
        changes.Stage(1, "one");
        PendingChangeSnapshot<int, string> before = changes.Snapshot();

        changes.Stage(1, "updated");
        changes.Stage(2, "two");

        PendingChangeEntry<int, string> item = Assert.Single(before.Items);
        Assert.Equal("one", item.Change);
        Assert.Equal(1, before.Version);
    }
}
