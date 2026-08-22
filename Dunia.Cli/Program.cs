using System.Globalization;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Archives.Recon;
using Dunia.Formats.Changes;
using Dunia.Formats.Fcb;
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
          verify    Verify archive invariants and round-trips

        Texture operations:
          dunia tex extract <input.xbt> <output.dds>

        FCB operations:
          dunia fcb probe <input.fcb>
          dunia fcb verify <input.fcb>
          dunia fcb scan <archive.fat> [--limit N]

        Archive operations:
          dunia probe <archive.fat>
          dunia list <archive.fat> [--limit N] [--names paths.txt]
          dunia entry <archive.fat> <entry-index> [--names paths.txt]
          dunia get <archive.fat> <entry-index> <output-file>
          dunia rebuild <source.fat> <output.fat> <entry-index> <replacement-file> [...]
          dunia apply <archive.fat> --dry-run <entry-index> <replacement-file> [...]
          dunia apply <archive.fat> --confirm-write <entry-index> <expected-hash> <replacement-file> [...]
          dunia verify roundtrip <archive.fat>
          dunia verify replacement <archive.fat> <entry-index>

        Hash operations:
          dunia hash compute <resource-path>
          dunia hash resolve <16-digit-hash> <paths.txt>
          dunia hash audit <archive.fat> <paths.txt>
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

        if (args is ["entry", var entryFatPath, var rawEntryIndex]
            && TryParseEntryIndex(rawEntryIndex, out int inspectedIndex))
        {
            return InspectEntry(entryFatPath, inspectedIndex, null);
        }

        if (args is ["entry", var namedEntryFatPath, var rawNamedEntryIndex, "--names", var entryNamesPath]
            && TryParseEntryIndex(rawNamedEntryIndex, out int namedInspectedIndex))
        {
            return InspectEntry(namedEntryFatPath, namedInspectedIndex, entryNamesPath);
        }

        if (args is ["tex", "extract", var xbtPath, var ddsPath])
        {
            return await ExtractDdsAsync(xbtPath, ddsPath).ConfigureAwait(false);
        }

        if (args is ["fcb", "probe", var fcbPath])
        {
            return ProbeFcb(fcbPath);
        }

        if (args is ["fcb", "verify", var verifiedFcbPath])
        {
            return VerifyFcb(verifiedFcbPath);
        }

        if (args is ["fcb", "scan", var scanFatPath])
        {
            return await ScanFcbAsync(scanFatPath, 100).ConfigureAwait(false);
        }

        if (args is ["fcb", "scan", var limitedScanFatPath, "--limit", var rawScanLimit]
            && int.TryParse(rawScanLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int scanLimit)
            && scanLimit > 0)
        {
            return await ScanFcbAsync(limitedScanFatPath, scanLimit).ConfigureAwait(false);
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

        if (args is ["hash", "audit", var auditFatPath, var auditNamesPath])
        {
            return AuditNames(auditFatPath, auditNamesPath);
        }

        if (args.Length >= 5 && args[0] == "rebuild" && (args.Length - 3) % 2 == 0)
        {
            return await RebuildAsync(args).ConfigureAwait(false);
        }

        if (args.Length >= 5 && args[0] == "apply" && args[2] == "--dry-run" && (args.Length - 3) % 2 == 0)
        {
            return await ApplyDryRunAsync(args).ConfigureAwait(false);
        }

        if (args.Length >= 6 && args[0] == "apply" && args[2] == "--confirm-write" && (args.Length - 3) % 3 == 0)
        {
            return await ApplyConfirmedAsync(args).ConfigureAwait(false);
        }

        if (args is ["verify", "roundtrip", var verifyFatPath])
        {
            return await VerifyRoundTripAsync(verifyFatPath).ConfigureAwait(false);
        }

        if (args is ["verify", "replacement", var replacementFatPath, var rawReplacementIndex]
            && TryParseEntryIndex(rawReplacementIndex, out int replacementIndex))
        {
            return await VerifyReplacementAsync(replacementFatPath, replacementIndex).ConfigureAwait(false);
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

    private static int ProbeFcb(string path)
    {
        try
        {
            using FileStream input = File.OpenRead(path);
            FcbDocument document = FcbReader.Read(input);
            Console.WriteLine($"path={Path.GetFullPath(path)}");
            Console.WriteLine(FormattableString.Invariant($"version={document.Header.Version}"));
            Console.WriteLine(FormattableString.Invariant($"flags={document.Header.Flags}"));
            Console.WriteLine(FormattableString.Invariant($"declared.objects={document.Header.DeclaredObjectCount}"));
            Console.WriteLine(FormattableString.Invariant($"declared.values={document.Header.DeclaredValueCount}"));
            Console.WriteLine(FormattableString.Invariant($"parsed.unique-nodes={document.UniqueNodeCount}"));
            Console.WriteLine(FormattableString.Invariant($"parsed.fields={document.FieldCount}"));
            Console.WriteLine(FormattableString.Invariant($"root.type-hash={document.Root.TypeHash:X8}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int VerifyFcb(string path)
    {
        try
        {
            using FileStream input = File.OpenRead(path);
            FcbRoundTripVerificationResult result = FcbRoundTripVerifier.Verify(input);
            Console.WriteLine(FormattableString.Invariant($"length={result.Length}"));
            Console.WriteLine($"source.sha256={result.SourceSha256}");
            Console.WriteLine($"output.sha256={result.OutputSha256}");
            Console.WriteLine($"byte-exact={result.IsByteExact.ToString().ToLowerInvariant()}");
            return result.IsByteExact ? 0 : 4;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> ScanFcbAsync(string fatPath, int limit)
    {
        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            await using FileStream fat = File.OpenRead(pair.FatPath);
            await using FileStream data = File.OpenRead(pair.DatPath);
            FatV10Index index = FatV10IndexReader.Read(fat, data.Length);
            FcbArchiveScanResult result = await FcbArchiveScanner.ScanAsync(data, index, limit)
                .ConfigureAwait(false);
            Console.WriteLine("index\thash\tuncompressed\tcompression");
            foreach (FcbArchiveMatch match in result.Matches)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"{match.EntryIndex}\t{match.NameHash:X16}\t{match.UncompressedSize}\t{match.CompressionScheme.ToString().ToLowerInvariant()}"));
            }

            Console.WriteLine(FormattableString.Invariant($"matches={result.Matches.Count}"));
            Console.WriteLine(FormattableString.Invariant($"scanned={result.ScannedEntryCount}"));
            Console.WriteLine(FormattableString.Invariant($"skipped={result.SkippedEntryCount}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
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
                string name = RenderName(names);

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

    private static int InspectEntry(string fatPath, int entryIndex, string? namesPath)
    {
        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            using FileStream input = File.OpenRead(pair.FatPath);
            FatV10Index index = FatV10IndexReader.Read(input, new FileInfo(pair.DatPath).Length);
            if ((uint)entryIndex >= (uint)index.Entries.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entryIndex),
                    entryIndex,
                    $"Entry index must be between 0 and {index.Entries.Count - 1}.");
            }

            FatV10Entry entry = index.Entries[entryIndex];
            DuniaNameResolver? resolver = namesPath is null ? null : LoadResolver(namesPath);
            Console.WriteLine(FormattableString.Invariant($"index={entryIndex}"));
            Console.WriteLine(FormattableString.Invariant($"hash={entry.NameHash:X16}"));
            Console.WriteLine($"name={RenderName(resolver?.Resolve(entry.NameHash) ?? [])}");
            Console.WriteLine(FormattableString.Invariant($"offset={entry.Offset}"));
            Console.WriteLine(FormattableString.Invariant($"stored={entry.StoredSize}"));
            Console.WriteLine(FormattableString.Invariant($"uncompressed={entry.UncompressedSize}"));
            Console.WriteLine($"compression={entry.CompressionScheme.ToString().ToLowerInvariant()}");
            Console.WriteLine($"encrypted={entry.IsEncrypted.ToString().ToLowerInvariant()}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int AuditNames(string fatPath, string namesPath)
    {
        try
        {
            ArchivePair pair = ArchivePair.FromIndex(fatPath);
            using FileStream input = File.OpenRead(pair.FatPath);
            FatV10Index index = FatV10IndexReader.Read(input, new FileInfo(pair.DatPath).Length);
            DuniaNameCoverageReport report = DuniaNameCoverageAnalyzer.Analyze(
                index.Entries.Select(entry => entry.NameHash),
                LoadResolver(namesPath));

            Console.WriteLine(FormattableString.Invariant($"entries={report.EntryCount}"));
            Console.WriteLine(FormattableString.Invariant($"resolved={report.ResolvedEntryCount}"));
            Console.WriteLine(FormattableString.Invariant($"unknown.entries={report.UnknownEntryCount}"));
            Console.WriteLine(FormattableString.Invariant($"unknown.hashes={report.UnknownHashes.Count}"));
            Console.WriteLine(FormattableString.Invariant($"collision.entries={report.CollisionEntryCount}"));
            Console.WriteLine(FormattableString.Invariant($"collision.hashes={report.CollisionHashes.Count}"));
            Console.WriteLine($"complete={report.IsComplete.ToString().ToLowerInvariant()}");
            foreach (ulong hash in report.UnknownHashes)
            {
                Console.WriteLine(FormattableString.Invariant($"unknown={hash:X16}"));
            }

            foreach (ulong hash in report.CollisionHashes)
            {
                Console.WriteLine(FormattableString.Invariant($"collision={hash:X16}"));
            }

            return report.IsComplete ? 0 : 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RebuildAsync(string[] args)
    {
        try
        {
            string stagingRoot = Path.Combine(Path.GetTempPath(), "DuniaToolkit");
            using var store = new ReplacementStagingStore(stagingRoot);
            Dictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
                ParseReplacementPaths(args, 3),
                store).ConfigureAwait(false);

            FatV10ArchivePatchFileBuildResult result = await FatV10ArchivePatchFileBuilder.BuildAsync(
                ArchivePair.FromIndex(args[1]),
                ArchivePair.FromIndex(args[2]),
                replacements,
                store).ConfigureAwait(false);
            Console.WriteLine($"fat={result.OutputPair.FatPath}");
            Console.WriteLine($"dat={result.OutputPair.DatPath}");
            Console.WriteLine(FormattableString.Invariant($"entries={result.Build.Index.Entries.Count}"));
            Console.WriteLine(FormattableString.Invariant($"replacements={result.Build.ReplacementCount}"));
            Console.WriteLine(FormattableString.Invariant($"dat.length={result.Build.DataLength}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> ApplyDryRunAsync(string[] args)
    {
        string? outputDirectory = null;

        try
        {
            string stagingRoot = Path.Combine(Path.GetTempPath(), "DuniaToolkit");
            using var store = new ReplacementStagingStore(stagingRoot);
            Dictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
                ParseReplacementPaths(args, 3),
                store).ConfigureAwait(false);
            outputDirectory = Path.Combine(stagingRoot, $"dryrun-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDirectory);
            var output = new ArchivePair(
                Path.Combine(outputDirectory, "verified.fat"),
                Path.Combine(outputDirectory, "verified.dat"));
            FatV10ArchivePatchFileBuildResult result = await FatV10ArchivePatchFileBuilder.BuildAsync(
                ArchivePair.FromIndex(args[1]),
                output,
                replacements,
                store).ConfigureAwait(false);

            Console.WriteLine("dry-run=true");
            Console.WriteLine("validated=true");
            Console.WriteLine("source.modified=false");
            Console.WriteLine(FormattableString.Invariant($"entries={result.Build.Index.Entries.Count}"));
            Console.WriteLine(FormattableString.Invariant($"replacements={result.Build.ReplacementCount}"));
            Console.WriteLine(FormattableString.Invariant($"dat.length={result.Build.DataLength}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (outputDirectory is not null && Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
        }
    }

    private static async Task<int> ApplyConfirmedAsync(string[] args)
    {
        try
        {
            ArchivePair target = ArchivePair.FromIndex(args[1]);
            Dictionary<int, (ulong ExpectedHash, string Path)> specifications =
                ParseConfirmedReplacementPaths(args, 3);
            using (FileStream fat = File.OpenRead(target.FatPath))
            {
                FatV10Index index = FatV10IndexReader.Read(fat, new FileInfo(target.DatPath).Length);
                foreach ((int entryIndex, (ulong expectedHash, _)) in specifications)
                {
                    if ((uint)entryIndex >= (uint)index.Entries.Count)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(args),
                            entryIndex,
                            $"Entry index must be between 0 and {index.Entries.Count - 1}.");
                    }

                    ulong actualHash = index.Entries[entryIndex].NameHash;
                    if (actualHash != expectedHash)
                    {
                        throw new InvalidDataException(
                            FormattableString.Invariant(
                                $"Entry {entryIndex} hash mismatch: expected {expectedHash:X16}, got {actualHash:X16}."));
                    }
                }
            }

            string stagingRoot = Path.Combine(Path.GetTempPath(), "DuniaToolkit");
            using var store = new ReplacementStagingStore(stagingRoot);
            Dictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
                specifications.ToDictionary(item => item.Key, item => item.Value.Path),
                store).ConfigureAwait(false);
            FatV10ArchivePatchApplyResult result = await FatV10ArchivePatchApplyService.ApplyAsync(
                target,
                replacements,
                store).ConfigureAwait(false);

            Console.WriteLine("applied=true");
            Console.WriteLine(FormattableString.Invariant($"replacements={result.Build.ReplacementCount}"));
            Console.WriteLine($"fat.backup={result.Backup.Fat.BackupPath}");
            Console.WriteLine($"fat.backup.sha256={result.Backup.Fat.Sha256}");
            Console.WriteLine($"dat.backup={result.Backup.Dat.BackupPath}");
            Console.WriteLine($"dat.backup.sha256={result.Backup.Dat.Sha256}");
            Console.WriteLine($"backup.created={result.Backup.CreatedAny.ToString().ToLowerInvariant()}");
            Console.WriteLine(FormattableString.Invariant($"dat.length={result.Build.DataLength}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> VerifyRoundTripAsync(string fatPath)
    {
        try
        {
            FatV10ArchiveRoundTripVerificationResult result = await FatV10ArchiveRoundTripVerifier.VerifyAsync(
                ArchivePair.FromIndex(fatPath),
                Path.Combine(Path.GetTempPath(), "DuniaToolkit")).ConfigureAwait(false);
            Console.WriteLine($"fat.source.sha256={result.SourceFatSha256}");
            Console.WriteLine($"fat.output.sha256={result.OutputFatSha256}");
            Console.WriteLine($"dat.source.sha256={result.SourceDatSha256}");
            Console.WriteLine($"dat.output.sha256={result.OutputDatSha256}");
            Console.WriteLine(FormattableString.Invariant($"fat.length={result.FatLength}"));
            Console.WriteLine(FormattableString.Invariant($"dat.length={result.DatLength}"));
            Console.WriteLine($"byte-exact={result.IsByteExact.ToString().ToLowerInvariant()}");
            return result.IsByteExact ? 0 : 4;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> VerifyReplacementAsync(string fatPath, int entryIndex)
    {
        try
        {
            FatV10ReplacementVerificationResult result = await FatV10ReplacementVerifier.VerifyAsync(
                ArchivePair.FromIndex(fatPath),
                entryIndex,
                Path.Combine(Path.GetTempPath(), "DuniaToolkit")).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant($"index={result.EntryIndex}"));
            Console.WriteLine(FormattableString.Invariant($"hash={result.NameHash:X16}"));
            Console.WriteLine($"source.compression={result.SourceCompression.ToString().ToLowerInvariant()}");
            Console.WriteLine($"source.payload.sha256={result.SourcePayloadSha256}");
            Console.WriteLine($"rebuilt.payload.sha256={result.RebuiltPayloadSha256}");
            Console.WriteLine($"unchanged.entries.exact={result.UnchangedEntriesExact.ToString().ToLowerInvariant()}");
            Console.WriteLine($"original.dat.prefix.exact={result.OriginalDataPrefixExact.ToString().ToLowerInvariant()}");
            Console.WriteLine(FormattableString.Invariant($"rebuilt.offset={result.RebuiltOffset}"));
            Console.WriteLine($"valid={result.IsValid.ToString().ToLowerInvariant()}");
            return result.IsValid ? 0 : 4;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<int, string> ParseReplacementPaths(string[] args, int startIndex)
    {
        var replacements = new Dictionary<int, string>();
        for (int i = startIndex; i < args.Length; i += 2)
        {
            if (!TryParseEntryIndex(args[i], out int index))
            {
                throw new ArgumentException($"Invalid replacement entry index: {args[i]}");
            }

            if (!replacements.TryAdd(index, args[i + 1]))
            {
                throw new ArgumentException($"Duplicate replacement entry index: {index}");
            }
        }

        return replacements;
    }

    private static Dictionary<int, (ulong ExpectedHash, string Path)> ParseConfirmedReplacementPaths(
        string[] args,
        int startIndex)
    {
        var replacements = new Dictionary<int, (ulong ExpectedHash, string Path)>();
        for (int i = startIndex; i < args.Length; i += 3)
        {
            if (!TryParseEntryIndex(args[i], out int index))
            {
                throw new ArgumentException($"Invalid replacement entry index: {args[i]}");
            }

            if (!TryParseHash(args[i + 1], out ulong expectedHash))
            {
                throw new ArgumentException($"Invalid expected entry hash: {args[i + 1]}");
            }

            if (!replacements.TryAdd(index, (expectedHash, args[i + 2])))
            {
                throw new ArgumentException($"Duplicate replacement entry index: {index}");
            }
        }

        return replacements;
    }

    private static async Task<Dictionary<int, StagedReplacement>> StageReplacementsAsync(
        IReadOnlyDictionary<int, string> paths,
        ReplacementStagingStore store)
    {
        var replacements = new Dictionary<int, StagedReplacement>();
        foreach ((int index, string path) in paths)
        {
            replacements.Add(index, await store.StageAsync(path).ConfigureAwait(false));
        }

        return replacements;
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

    private static bool TryParseEntryIndex(string value, out int index) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;

    private static string RenderName(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "<unknown>",
        1 => names[0],
        _ => $"<collision:{string.Join('|', names)}>",
    };

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
