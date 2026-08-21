using System.Buffers.Binary;
using System.Text;

namespace Dunia.Formats.Archives.Recon;

public static class FatPrefixProbe
{
    public const int DefaultPrefixLength = 64;
    public const int MaximumPrefixLength = 4096;

    public static FatPrefix Read(Stream input, int prefixLength = DefaultPrefixLength)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(input));
        }

        if (prefixLength is < 8 or > MaximumPrefixLength)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        byte[] prefix = new byte[prefixLength];
        int length = 0;

        while (length < prefix.Length)
        {
            int read = input.Read(prefix, length, prefix.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length < 8)
        {
            throw new InvalidDataException("FAT probe requires at least 8 bytes.");
        }

        Array.Resize(ref prefix, length);
        return new(
            Encoding.ASCII.GetString(prefix, 0, 4),
            BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32BigEndian(prefix.AsSpan(4, 4)),
            prefix);
    }
}

public sealed record FatPrefix(
    string MagicAscii,
    int VersionLittleEndian,
    int VersionBigEndian,
    ReadOnlyMemory<byte> Bytes)
{
    public string Hex => Convert.ToHexString(Bytes.Span);
}

