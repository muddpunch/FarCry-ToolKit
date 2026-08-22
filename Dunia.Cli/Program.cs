using System.Globalization;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Archives.Recon;
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
          dunia list <archive.fat> [--limit N]
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
            return ListEntries(listFatPath, 100);
        }

        if (args is ["list", var limitedFatPath, "--limit", var rawLimit]
            && int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int limit)
            && limit > 0)
        {
            return ListEntries(limitedFatPath, limit);
        }

        if (args is ["tex", "extract", var xbtPath, var ddsPath])
        {
            return await ExtractDdsAsync(xbtPath, ddsPath).ConfigureAwait(false);
        }

        Console.Error.WriteLine("Invalid or unavailable command. Use --help for usage.");
        return 2;
    }

    private static int ListEntries(string fatPath, int limit)
    {
        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            long datLength = new FileInfo(pair.DatPath).Length;

            using FileStream input = File.OpenRead(pair.FatPath);
            FatV10Index index = FatV10IndexReader.Read(input, datLength);

            Console.WriteLine("index\thash\toffset\tstored\tuncompressed\tcompression\tencrypted");
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

                Console.WriteLine(FormattableString.Invariant(
                    $"{i}\t{entry.NameHash:X16}\t{entry.Offset}\t{entry.StoredSize}\t{entry.UncompressedSize}\t{compression}\t{entry.IsEncrypted.ToString().ToLowerInvariant()}"));
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
