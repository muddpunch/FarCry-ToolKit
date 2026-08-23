using System.Buffers.Binary;
using System.Text;

namespace Dunia.Formats.Meshes;

public static class XbgSummaryReader
{
    public const uint FarCry5Version = 0x000D0047;
    private const int FileHeaderSize = 32;
    private const int ChunkHeaderSize = 20;

    public static XbgSummary Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("XBG input must be readable and seekable.", nameof(input));
        }

        if (input.Length < FileHeaderSize)
        {
            throw new InvalidDataException("XBG header is truncated.");
        }

        input.Position = 0;
        Span<byte> header = stackalloc byte[FileHeaderSize];
        input.ReadExactly(header);
        if (!header[..4].SequenceEqual("HSEM"u8))
        {
            throw new InvalidDataException("Expected an HSEM-signed XBG file.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (version != FarCry5Version)
        {
            throw new NotSupportedException($"Unsupported XBG version 0x{version:X8}.");
        }

        uint resourceHash = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        uint rawChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        if (rawChunkCount > 4096)
        {
            throw new InvalidDataException($"XBG chunk count {rawChunkCount} exceeds the safety limit.");
        }

        int chunkCount = checked((int)rawChunkCount);
        var chunks = new List<XbgChunkInfo>(chunkCount);
        int materialCount = 0;
        int lodCount = 0;
        long offset = FileHeaderSize;
        Span<byte> chunkHeader = stackalloc byte[ChunkHeaderSize];
        Span<byte> countBuffer = stackalloc byte[4];

        for (int i = 0; i < chunkCount; i++)
        {
            if (offset > input.Length - ChunkHeaderSize)
            {
                throw new InvalidDataException($"XBG chunk {i} header exceeds the file bounds.");
            }

            input.Position = offset;
            input.ReadExactly(chunkHeader);
            string name = Encoding.ASCII.GetString(chunkHeader[..4]).TrimEnd('\0');
            uint chunkVersion = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[8..]);
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[12..]);
            if (chunkSize < ChunkHeaderSize || chunkSize > input.Length - offset)
            {
                throw new InvalidDataException($"XBG chunk {name} at 0x{offset:X} has an invalid size {chunkSize}.");
            }

            if (dataSize > chunkSize - ChunkHeaderSize)
            {
                throw new InvalidDataException($"XBG chunk {name} data exceeds its chunk envelope.");
            }

            chunks.Add(new(name, offset, chunkVersion, chunkSize, dataSize));
            if (name is "LTMR" or "SDOL")
            {
                input.ReadExactly(countBuffer);
                int count = BinaryPrimitives.ReadInt32LittleEndian(countBuffer);
                if (count < 0)
                {
                    throw new InvalidDataException($"XBG chunk {name} contains a negative item count.");
                }

                if (name == "LTMR")
                {
                    materialCount = count;
                }
                else
                {
                    lodCount = count;
                }
            }

            offset += chunkSize;
        }

        if (!chunks.Any(chunk => chunk.Name == "SDOL"))
        {
            throw new InvalidDataException("XBG does not contain an SDOL geometry chunk.");
        }

        return new(
            version,
            resourceHash,
            declaredSize,
            input.Length,
            materialCount,
            lodCount,
            chunks.AsReadOnly());
    }
}
