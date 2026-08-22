namespace Dunia.Formats.Fcb;

public sealed class FcbField
{
    internal FcbField(uint nameHash, byte[] data, long sourceOffset, FcbField? referenceTarget)
    {
        NameHash = nameHash;
        Data = data;
        SourceOffset = sourceOffset;
        ReferenceTarget = referenceTarget;
    }

    public uint NameHash { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public long SourceOffset { get; }

    public FcbField? ReferenceTarget { get; }

    public bool IsReference => ReferenceTarget is not null;
}
