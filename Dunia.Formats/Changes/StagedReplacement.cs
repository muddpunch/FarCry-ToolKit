namespace Dunia.Formats.Changes;

public sealed class StagedReplacement
{
    internal StagedReplacement(Guid id, string originalFileName, long length, string sha256)
    {
        Id = id;
        OriginalFileName = originalFileName;
        Length = length;
        Sha256 = sha256;
    }

    public Guid Id { get; }

    public string OriginalFileName { get; }

    public long Length { get; }

    public string Sha256 { get; }
}

