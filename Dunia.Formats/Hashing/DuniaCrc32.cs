namespace Dunia.Formats.Hashing;

// Algorithm adapted from Gibbed.Dunia's zlib-licensed CRC32 implementation.
public static class DuniaCrc32
{
    private const uint Polynomial = 0xEDB88320U;
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        uint hash = uint.MaxValue;
        foreach (char c in value)
        {
            hash = Table[(byte)hash ^ (byte)c] ^ (hash >> 8);
        }

        return ~hash;
    }

    public static uint Compute(ReadOnlySpan<byte> value)
    {
        uint hash = uint.MaxValue;
        foreach (byte b in value)
        {
            hash = Table[(byte)hash ^ b] ^ (hash >> 8);
        }

        return ~hash;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (int i = 0; i < table.Length; i++)
        {
            uint value = (uint)i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
