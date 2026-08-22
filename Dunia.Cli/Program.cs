using System.Globalization;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Archives.Recon;
using Dunia.Formats.Hashing;
using Dunia.Formats.Textures;

namespace Dunia.Cli;

internal static class Program
{
    private const string Usage = """
        Dunia Toolkit CLI

        Usage:
          dunia <command> [options]

        Commands:
          probe     Print an archive prefix for Phase-0 recon
          list      List archive entries
          entry     Inspect an archive entry
          get       Extract an archive entry
          tex       Texture operations
          pack      Pack changed resources
          rebuild   Rebuild an archive pair
          refs      Resolve resource references
          hash      Compute or resolve Dunia hashes

        Texture operations:
          dunia tex extract <input.xbt> <output.dds>

        Archive operations:
          dunia probe <archive.fat>
          dunia list <archive.fat> [--limit N] [--names paths.txt]
          dunia get <archive.fat> <entry-index> <output-file>

        Hash operations:
          dunia hash compute <resource-path>
          dunia hash resolve <16-digit-hash> <paths.txt>
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args is ["probe", var fatPath])
        {
            return Probe(fatPath);
        }

        if (args is ["list", var listFatPath])
        {
            return ListEntries(listFatPath, 100, null);
        }

        if (args is ["list", var limitedFatPath, "--limit", var rawLimit]
            && int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int limit)
            && limit > 0)
        {
            return ListEntries(limitedFatPath, limit, null);
        }

        if (args is ["list", var namedFatPath, "--names", var namesPath])
        {
            return ListEntries(namedFatPath, 100, namesPath);
        }

        if (args is ["list", var namedLimitedFatPath, "--limit", var namedRawLimit, "--names", var limitedNamesPath]
            && int.TryParse(namedRawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int namedLimit)
            && namedLimit > 0)
        {
            return ListEntries(namedLimitedFatPath, namedLimit, limitedNamesPath);
        }

        if (args is ["list", var reversedFatPath, "--names", var reversedNamesPath, "--limit", var reversedRawLimit]
            && int.TryParse(reversedRawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int reversedLimit)
            && reversedLimit > 0)
        {
            return ListEntries(reversedFatPath, reversedLimit, reversedNamesPath);
        }

        if (args is ["get", var getFatPath, var rawIndex, var outputPath]
            && int.TryParse(rawIndex, NumberStyles.None, CultureInfo.InvariantCulture, out int entryIndex)
            && entryIndex >= 0)
        {
            return await ExtractEntryAsync(getFatPath, entryIndex, outputPath).ConfigureAwait(false);
        }

        if (args is ["tex", "extract", var xbtPath, var ddsPath])
        {
            return await ExtractDdsAsync(xbtPath, ddsPath).ConfigureAwait(false);
        }

        if (args is ["hash", "compute", var resourcePath])
        {
            string normalized = DuniaPathHash.Normalize(resourcePath);
            Console.WriteLine(FormattableString.Invariant($"hash={DuniaCrc64.Compute(normalized):X16}"));
            Console.WriteLine($"path={normalized}");
            return 0;
        }

        if (args is ["hash", "resolve", var rawHash, var hashNamesPath]
            && TryParseHash(rawHash, out ulong hash))
        {
            return ResolveHash(hash, hashNamesPath);
        }

        Console.Error.WriteLine("Invalid or unavailable command. Use --help for usage.");
        return 2;
    }

    private static async Task<int> ExtractEntryAsync(string fatPath, int entryIndex, string outputPath)
    {
        string? temporaryPath = null;

        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            long datLength = new FileInfo(pair.DatPath).Length;

            using FileStream fat = File.OpenRead(pair.FatPath);
            FatV10Index index = FatV10IndexReader.Read(fat, datLength);
            if ((uint)entryIndex >= (uint)index.Entries.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entryIndex),
                    entryIndex,
                    $"Entry index must be between 0 and {index.Entries.Count - 1}.");
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            if (File.Exists(fullOutputPath))
            {
                throw new IOException("Output file already exists.");
            }

            temporaryPath = $"{fullOutputPath}.{Guid.NewGuid():N}.tmp";
            await using FileStream data = File.OpenRead(pair.DatPath);
            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                80 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                FatV10Entry entry = index.Entries[entryIndex];
                await FatV10PayloadExtractor.ExtractAsync(data, entry, output).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                output.Flush(true);
            }

            File.Move(temporaryPath, fullOutputPath, false);
            FatV10Entry extracted = index.Entries[entryIndex];
            Console.WriteLine($"output={fullOutputPath}");
            Console.WriteLine(FormattableString.Invariant($"index={entryIndex}"));
            Console.WriteLine(FormattableString.Invariant($"hash={extracted.NameHash:X16}"));
            Console.WriteLine(FormattableString.Invariant($"length={extracted.UncompressedSize}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int ListEntries(string fatPath, int limit, string? namesPath)
    {
        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            long datLength = new FileInfo(pair.DatPath).Length;

            using FileStream input = File.OpenRead(pair.FatPath);
            FatV10Index index = FatV10IndexReader.Read(input, datLength);
            DuniaNameResolver? resolver = namesPath is null ? null : LoadResolver(namesPath);

            Console.WriteLine("index\thash\tname\toffset\tstored\tuncompressed\tcompression\tencrypted");
            int shown = Math.Min(index.Entries.Count, limit);
            for (int i = 0; i < shown; i++)
            {
                FatV10Entry entry = index.Entries[i];
                string compression = entry.CompressionScheme switch
                {
                    FatV10CompressionScheme.None => "none",
                    FatV10CompressionScheme.Lz4 => "lz4",
                    _ => throw new InvalidDataException($"Unsupported compression scheme: {entry.CompressionScheme}.")
                };
                IReadOnlyList<string> names = resolver?.Resolve(entry.NameHash) ?? [];
                string name = names.Count switch
                {
                    0 => "<unknown>",
                    1 => names[0],
                    _ => $"<collision:{string.Join('|', names)}>",
                };

                Console.WriteLine(FormattableString.Invariant(
                    $"{i}\t{entry.NameHash:X16}\t{name}\t{entry.Offset}\t{entry.StoredSize}\t{entry.UncompressedSize}\t{compression}\t{entry.IsEncrypted.ToString().ToLowerInvariant()}"));
            }

            Console.WriteLine(FormattableString.Invariant($"shown={shown}"));
            Console.WriteLine(FormattableString.Invariant($"total={index.Entries.Count}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int ResolveHash(ulong hash, string namesPath)
    {
        try
        {
            IReadOnlyList<string> names = LoadResolver(namesPath).Resolve(hash);
            Console.WriteLine(FormattableString.Invariant($"hash={hash:X16}"));
            foreach (string name in names)
            {
                Console.WriteLine($"path={name}");
            }

            Console.WriteLine(FormattableString.Invariant($"matches={names.Count}"));
            return names.Count > 0 ? 0 : 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static DuniaNameResolver LoadResolver(string namesPath)
    {
        using StreamReader input = File.OpenText(namesPath);
        return DuniaNameResolver.Load(input);
    }

    private static bool TryParseHash(string value, out ulong hash)
    {
        hash = 0;
        ReadOnlySpan<char> span = value.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        return span.Length is > 0 and <= 16
            && ulong.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hash);
    }

    private static int Probe(string fatPath)
    {
        try
        {
            using FileStream input = File.OpenRead(fatPath);
            FatPrefix prefix = FatPrefixProbe.Read(input);

            Console.WriteLine($"path={Path.GetFullPath(fatPath)}");
            Console.WriteLine($"length={input.Length}");
            Console.WriteLine($"magic.ascii={prefix.MagicAscii}");
            Console.WriteLine($"version.le={prefix.VersionLittleEndian}");
            Console.WriteLine($"version.be={prefix.VersionBigEndian}");
            Console.WriteLine($"prefix.hex={prefix.Hex}");

            if (prefix.VersionLittleEndian == FatV10IndexSummaryReader.Version)
            {
                FatV10IndexSummary summary = FatV10IndexSummaryReader.Read(input);
                Console.WriteLine($"platform={summary.Platform}");
                Console.WriteLine($"entries={summary.EntryCount}");
                Console.WriteLine($"entry.size={summary.EntrySize}");
                Console.WriteLine($"layout.valid=true");

                string datPath = Path.ChangeExtension(fatPath, ".dat");
                long? datLength = File.Exists(datPath) ? new FileInfo(datPath).Length : null;
                FatV10Index index = FatV10IndexReader.Read(input, datLength);
                Console.WriteLine($"entries.lz4={index.Entries.Count(entry => entry.CompressionScheme == FatV10CompressionScheme.Lz4)}");
                Console.WriteLine($"entries.encrypted={index.Entries.Count(entry => entry.IsEncrypted)}");
                Console.WriteLine($"dat.bounds.valid={datLength.HasValue}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> ExtractDdsAsync(string xbtPath, string ddsPath)
    {
        string? temporaryPath = null;

        try
        {
            string fullXbtPath = Path.GetFullPath(xbtPath);
            string fullDdsPath = Path.GetFullPath(ddsPath);

            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(fullXbtPath, fullDdsPath, pathComparison))
            {
                throw new ArgumentException("Input and output paths must be different.");
            }

            if (File.Exists(fullDdsPath))
            {
                throw new IOException("Output file already exists.");
            }

            temporaryPath = $"{fullDdsPath}.{Guid.NewGuid():N}.tmp";
            await using FileStream input = File.OpenRead(fullXbtPath);
            XbtDdsExtractionResult result;

            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                80 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                result = await XbtDdsExtractor.ExtractAsync(input, output).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                output.Flush(true);
            }

            File.Move(temporaryPath, fullDdsPath, false);

            Console.WriteLine($"output={fullDdsPath}");
            Console.WriteLine($"header.length={result.HeaderLength}");
            Console.WriteLine($"dds.length={result.DdsLength}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
