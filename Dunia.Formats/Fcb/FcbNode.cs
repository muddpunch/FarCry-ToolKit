namespace Dunia.Formats.Fcb;

public sealed class FcbNode
{
    private readonly List<FcbField> fields = [];
    private readonly List<FcbNode> children = [];

    internal FcbNode(long sourceOffset) => SourceOffset = sourceOffset;

    public long SourceOffset { get; }

    public uint TypeHash { get; internal set; }

    public IReadOnlyList<FcbField> Fields => fields;

    public IReadOnlyList<FcbNode> Children => children;

    internal List<FcbField> MutableFields => fields;

    internal List<FcbNode> MutableChildren => children;
}
