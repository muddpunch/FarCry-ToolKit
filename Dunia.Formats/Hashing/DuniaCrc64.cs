namespace Dunia.Formats.Hashing;

// Algorithm adapted from Gibbed.Dunia's zlib-licensed CRC64 implementation.
public static class DuniaCrc64
{
    private const ulong Polynomial = 0xD800000000000000UL;
    private static readonly ulong[] Table = CreateTable();

    public static ulong Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ulong hash = 0;
        foreach (char c in value)
        {
            hash = Table[(byte)hash ^ (byte)c] ^ (hash >> 8);
        }

        return hash;
    }

    private static ulong[] CreateTable()
    {
        var table = new ulong[256];
        for (int i = 0; i < table.Length; i++)
        {
            ulong value = (byte)i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
