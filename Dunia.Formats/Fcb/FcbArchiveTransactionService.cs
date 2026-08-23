using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveTransactionService
{
    public const int ApiVersion = 1;

    private const int BufferSize = 1024 * 1024;

    public static async Task<FcbArchiveTransactionPlanResult> PlanAsync(
        ArchivePair source,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        CancellationToken cancellationToken = default) =>
        (await CreateArtifactAsync(source, schema, entries, cancellationToken).ConfigureAwait(false)).Plan;

    public static async Task<FcbArchiveTransactionDryRunResult> DryRunAsync(
        ArchivePair source,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        FcbArchiveTransactionArtifact artifact = await CreateArtifactAsync(
            source,
            schema,
            entries,
            cancellationToken).ConfigureAwait(false);
        return await DryRunAsync(
            source,
            schema,
            artifact,
            temporaryRoot,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<FcbArchiveTransactionCopyResult> CreateCopyAsync(
        ArchivePair source,
        ArchivePair destination,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateDestination(source, destination);
        FcbArchiveTransactionArtifact artifact = await CreateArtifactAsync(
            source,
            schema,
            entries,
            cancellationToken).ConfigureAwait(false);
        FcbArchiveTransactionDryRunResult dryRun = await DryRunAsync(
            source,
            schema,
            artifact,
            temporaryRoot,
            cancellationToken).ConfigureAwait(false);
        if (!dryRun.IsVerified)
        {
            throw new InvalidDataException("FCB archive transaction dry-run verification failed.");
        }

        bool published = false;
        try
        {
            using var store = new ReplacementStagingStore(Path.GetFullPath(temporaryRoot));
            IReadOnlyDictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
                artifact,
                store,
                cancellationToken).ConfigureAwait(false);
            await FatV10ArchivePatchFileBuilder.BuildAsync(
                source,
                destination,
                replacements,
                store,
                (lockedSource, token) => ValidateLockedSourceAsync(
                    lockedSource,
                    schema,
                    artifact,
                    token),
                cancellationToken).ConfigureAwait(false);
            published = true;
            await ValidatePublishedAsync(
                destination,
                schema,
                artifact,
                cancellationToken).ConfigureAwait(false);
            return new(destination, artifact.Plan, true);
        }
        catch
        {
            if (published)
            {
                File.Delete(destination.FatPath);
                File.Delete(destination.DatPath);
            }

            throw;
        }
    }

    public static async Task<FcbArchiveTransactionApplyResult> ApplyAsync(
        ArchivePair target,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        string expectedPlanSha256,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        expectedPlanSha256 = NormalizeSha256(expectedPlanSha256, nameof(expectedPlanSha256));
        FcbArchiveTransactionArtifact artifact = await CreateArtifactAsync(
            target,
            schema,
            entries,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(artifact.Plan.PlanSha256, expectedPlanSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"Transaction plan SHA-256 mismatch: expected {expectedPlanSha256}, got {artifact.Plan.PlanSha256}."));
        }

        if (artifact.Plan.NoOp)
        {
            return new(target, null, artifact.Plan, true, true);
        }

        FcbArchiveTransactionDryRunResult dryRun = await DryRunAsync(
            target,
            schema,
            artifact,
            temporaryRoot,
            cancellationToken).ConfigureAwait(false);
        if (!dryRun.IsVerified)
        {
            throw new InvalidDataException("FCB archive transaction dry-run verification failed.");
        }

        bool semanticVerified = false;
        using var store = new ReplacementStagingStore(Path.GetFullPath(temporaryRoot));
        IReadOnlyDictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
            artifact,
            store,
            cancellationToken).ConfigureAwait(false);
        FatV10ArchivePatchApplyResult apply = await FatV10ArchivePatchApplyService.ApplyAsync(
            target,
            replacements,
            store,
            (lockedSource, token) => ValidateLockedSourceAsync(
                lockedSource,
                schema,
                artifact,
                token),
            async (published, token) =>
            {
                await ValidatePublishedAsync(published, schema, artifact, token).ConfigureAwait(false);
                semanticVerified = true;
            },
            cancellationToken).ConfigureAwait(false);
        return new(target, apply.Backup, artifact.Plan, false, semanticVerified);
    }

    private static async Task<FcbArchiveTransactionArtifact> CreateArtifactAsync(
        ArchivePair source,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveTransactionEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.FatPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.DatPath);
        cancellationToken.ThrowIfCancellationRequested();

        FcbArchiveTransactionEntry[] requests = SnapshotRequests(entries);
        var artifacts = new List<FcbArchiveBatchMutationArtifact>(requests.Length);
        int? archiveEntryCount = null;
        long? sourceDataLength = null;
        foreach (FcbArchiveTransactionEntry request in requests)
        {
            FcbArchiveBatchMutationArtifact artifact = await
                FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                    source,
                    request.EntryIndex,
                    request.ExpectedResourceNameHash,
                    schema,
                    request.Mutations,
                    cancellationToken).ConfigureAwait(false);
            if (archiveEntryCount is not null && archiveEntryCount != artifact.Plan.ArchiveEntryCount ||
                sourceDataLength is not null && sourceDataLength != artifact.SourceDataLength)
            {
                throw new InvalidDataException("Archive changed while the transaction plan was being created.");
            }

            archiveEntryCount = artifact.Plan.ArchiveEntryCount;
            sourceDataLength = artifact.SourceDataLength;
            artifacts.Add(artifact);
        }

        IReadOnlyList<FcbArchiveBatchMutationPlanResult> plans = Array.AsReadOnly(
            artifacts.Select(artifact => artifact.Plan).ToArray());
        string planSha256 = ComputePlanSha256(archiveEntryCount!.Value, plans);
        var plan = new FcbArchiveTransactionPlanResult(
            ApiVersion,
            archiveEntryCount.Value,
            plans,
            planSha256,
            plans.All(entry => entry.NoOp));
        return new(requests, artifacts.AsReadOnly(), plan, sourceDataLength!.Value);
    }

    private static FcbArchiveTransactionEntry[] SnapshotRequests(
        IReadOnlyList<FcbArchiveTransactionEntry> entries)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("At least one FCB archive transaction entry is required.", nameof(entries));
        }

        var seenEntries = new HashSet<int>();
        var result = new List<FcbArchiveTransactionEntry>(entries.Count);
        foreach (FcbArchiveTransactionEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Mutations);
            if (entry.EntryIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "Archive entry indexes cannot be negative.");
            }

            if (!seenEntries.Add(entry.EntryIndex))
            {
                throw new ArgumentException(
                    $"Archive entry {entry.EntryIndex} is targeted more than once.",
                    nameof(entries));
            }

            FcbArchiveFieldMutation[] mutations = entry.Mutations
                .Select(mutation =>
                {
                    ArgumentNullException.ThrowIfNull(mutation);
                    ArgumentNullException.ThrowIfNull(mutation.Value);
                    return new FcbArchiveFieldMutation(
                        mutation.NodeIndex,
                        mutation.FieldIndex,
                        mutation.ExpectedTypeHash,
                        mutation.ExpectedFieldHash,
                        mutation.Value);
                })
                .OrderBy(mutation => mutation.NodeIndex)
                .ThenBy(mutation => mutation.FieldIndex)
                .ToArray();
            result.Add(new(entry.EntryIndex, entry.ExpectedResourceNameHash, mutations));
        }

        return result.OrderBy(entry => entry.EntryIndex).ToArray();
    }

    private static string ComputePlanSha256(
        int archiveEntryCount,
        IReadOnlyList<FcbArchiveBatchMutationPlanResult> entries)
    {
        var canonical = new StringBuilder();
        canonical.Append("DUNIA-FCB-ARCHIVE-TRANSACTION-V1\n");
        canonical.Append("archive.entries=")
            .Append(archiveEntryCount.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (FcbArchiveBatchMutationPlanResult entry in entries)
        {
            canonical.Append("entry=").Append(entry.EntryIndex.ToString(CultureInfo.InvariantCulture))
                .Append(';').Append(entry.ResourceNameHash.ToString("X16", CultureInfo.InvariantCulture))
                .Append(';').Append(entry.SourcePayloadLength.ToString(CultureInfo.InvariantCulture))
                .Append(';').Append(entry.SourcePayloadSha256)
                .Append(';').Append(entry.PlannedPayloadLength.ToString(CultureInfo.InvariantCulture))
                .Append(';').Append(entry.PlannedPayloadSha256)
                .Append('\n');
            foreach (FcbArchivePlannedFieldMutation mutation in entry.Mutations)
            {
                canonical.Append("field=").Append(mutation.NodeIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(mutation.FieldIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(mutation.TypeHash.ToString("X8", CultureInfo.InvariantCulture))
                    .Append(';').Append(mutation.FieldHash.ToString("X8", CultureInfo.InvariantCulture))
                    .Append(';').Append(mutation.Codec)
                    .Append(';').Append(mutation.RequestedEncodedHex)
                    .Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static async Task<FcbArchiveTransactionDryRunResult> DryRunAsync(
        ArchivePair source,
        FcbValueSchema schema,
        FcbArchiveTransactionArtifact artifact,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        cancellationToken.ThrowIfCancellationRequested();
        int replacementCount = artifact.Entries.Count(entry => !entry.Plan.NoOp);
        if (replacementCount == 0)
        {
            return new(artifact.Plan, 0, true, true, true);
        }

        string root = Path.GetFullPath(temporaryRoot);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, $"fcb-transaction-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionPath);
        try
        {
            var outputPair = new ArchivePair(
                Path.Combine(sessionPath, "verified.fat"),
                Path.Combine(sessionPath, "verified.dat"));
            FatV10ArchivePatchFileBuildResult build;
            using (var store = new ReplacementStagingStore(sessionPath))
            {
                IReadOnlyDictionary<int, StagedReplacement> replacements = await StageReplacementsAsync(
                    artifact,
                    store,
                    cancellationToken).ConfigureAwait(false);
                build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                    source,
                    outputPair,
                    replacements,
                    store,
                    (lockedSource, token) => ValidateLockedSourceAsync(
                        lockedSource,
                        schema,
                        artifact,
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            bool payloadsExact = await ValidatePayloadsExactAsync(
                outputPair,
                build.Build.Index,
                artifact,
                cancellationToken).ConfigureAwait(false);
            bool untouchedEntriesExact = EntriesExceptTargetsMatch(
                artifact.Entries[0].SourceIndex.Entries,
                build.Build.Index.Entries,
                artifact.Entries
                    .Where(entry => !entry.Plan.NoOp)
                    .Select(entry => entry.Plan.EntryIndex)
                    .ToHashSet());
            bool prefixExact = await PrefixMatchesAsync(
                source.DatPath,
                outputPair.DatPath,
                artifact.SourceDataLength,
                cancellationToken).ConfigureAwait(false);
            var result = new FcbArchiveTransactionDryRunResult(
                artifact.Plan,
                replacementCount,
                payloadsExact,
                untouchedEntriesExact,
                prefixExact);
            if (!result.IsVerified)
            {
                throw new InvalidDataException("FCB archive transaction dry-run verification failed.");
            }

            return result;
        }
        finally
        {
            if (Directory.Exists(sessionPath))
            {
                Directory.Delete(sessionPath, true);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<int, StagedReplacement>> StageReplacementsAsync(
        FcbArchiveTransactionArtifact artifact,
        ReplacementStagingStore store,
        CancellationToken cancellationToken)
    {
        var replacements = new Dictionary<int, StagedReplacement>();
        foreach (FcbArchiveBatchMutationArtifact entry in artifact.Entries.Where(entry => !entry.Plan.NoOp))
        {
            using var payload = new MemoryStream(entry.PlannedPayload, false);
            StagedReplacement replacement = await store.StageAsync(
                payload,
                $"entry-{entry.Plan.EntryIndex}.fcb",
                cancellationToken).ConfigureAwait(false);
            replacements.Add(entry.Plan.EntryIndex, replacement);
        }

        return replacements;
    }

    private static async Task ValidateLockedSourceAsync(
        ArchivePair lockedSource,
        FcbValueSchema schema,
        FcbArchiveTransactionArtifact expected,
        CancellationToken cancellationToken)
    {
        FcbArchiveTransactionArtifact actual = await CreateArtifactAsync(
            lockedSource,
            schema,
            expected.Requests,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual.Plan.PlanSha256, expected.Plan.PlanSha256, StringComparison.Ordinal) ||
            actual.Entries.Count != expected.Entries.Count)
        {
            throw new InvalidDataException("Locked FCB archive transaction differs from the verified plan.");
        }

        for (int i = 0; i < actual.Entries.Count; i++)
        {
            if (!actual.Entries[i].PlannedPayload.AsSpan().SequenceEqual(expected.Entries[i].PlannedPayload))
            {
                throw new InvalidDataException(
                    "Locked FCB archive transaction payload differs from the verified plan.");
            }
        }
    }

    private static async Task ValidatePublishedAsync(
        ArchivePair published,
        FcbValueSchema schema,
        FcbArchiveTransactionArtifact artifact,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(published.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(published.DatPath).Length);
        }

        await using FileStream data = File.OpenRead(published.DatPath);
        foreach (FcbArchiveBatchMutationArtifact entry in artifact.Entries)
        {
            if ((uint)entry.Plan.EntryIndex >= (uint)index.Entries.Count ||
                index.Entries[entry.Plan.EntryIndex].NameHash != entry.Plan.ResourceNameHash)
            {
                throw new InvalidDataException("A published transaction resource identity is invalid.");
            }

            await using var payload = new MemoryStream(entry.PlannedPayload.Length);
            await FatV10PayloadExtractor.ExtractAsync(
                data,
                index.Entries[entry.Plan.EntryIndex],
                payload,
                cancellationToken).ConfigureAwait(false);
            if (!payload.GetBuffer().AsSpan(0, checked((int)payload.Length))
                .SequenceEqual(entry.PlannedPayload))
            {
                throw new InvalidDataException(
                    "A published transaction payload differs from the verified plan.");
            }

            payload.Position = 0;
            FcbDocument document = FcbReader.Read(payload);
            IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
            foreach (FcbArchivePlannedFieldMutation mutation in entry.Plan.Mutations)
            {
                if ((uint)mutation.NodeIndex >= (uint)nodes.Count ||
                    (uint)mutation.FieldIndex >= (uint)nodes[mutation.NodeIndex].Fields.Count)
                {
                    throw new InvalidDataException("A published transaction field is missing.");
                }

                FcbNode node = nodes[mutation.NodeIndex];
                FcbField field = node.Fields[mutation.FieldIndex];
                if (node.TypeHash != mutation.TypeHash ||
                    field.NameHash != mutation.FieldHash ||
                    field.IsReference ||
                    !schema.TryResolve(node.TypeHash, field.NameHash, out FcbValueKind codec) ||
                    codec != mutation.Codec ||
                    !field.Data.Span.SequenceEqual(FcbValueEncoder.Encode(codec, mutation.RequestedValue)))
                {
                    throw new InvalidDataException("Published transaction semantic validation failed.");
                }
            }

            if (!FcbValueSchemaCoverageAnalyzer.Analyze(document, schema).IsComplete)
            {
                throw new InvalidDataException("Published transaction schema coverage is incomplete.");
            }

            payload.Position = 0;
            if (!FcbRoundTripVerifier.Verify(payload).IsByteExact)
            {
                throw new InvalidDataException(
                    "A published transaction FCB failed byte-exact round-trip verification.");
            }
        }
    }

    private static async Task<bool> ValidatePayloadsExactAsync(
        ArchivePair pair,
        FatV10Index index,
        FcbArchiveTransactionArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using FileStream data = File.OpenRead(pair.DatPath);
        foreach (FcbArchiveBatchMutationArtifact entry in artifact.Entries)
        {
            await using var payload = new MemoryStream(entry.PlannedPayload.Length);
            await FatV10PayloadExtractor.ExtractAsync(
                data,
                index.Entries[entry.Plan.EntryIndex],
                payload,
                cancellationToken).ConfigureAwait(false);
            if (!payload.GetBuffer().AsSpan(0, checked((int)payload.Length))
                .SequenceEqual(entry.PlannedPayload))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntriesExceptTargetsMatch(
        IReadOnlyList<FatV10Entry> source,
        IReadOnlyList<FatV10Entry> rebuilt,
        HashSet<int> targetIndexes)
    {
        if (source.Count != rebuilt.Count)
        {
            return false;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if ((!targetIndexes.Contains(i) && source[i] != rebuilt[i]) ||
                (targetIndexes.Contains(i) && source[i].NameHash != rebuilt[i].NameHash))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> PrefixMatchesAsync(
        string sourcePath,
        string rebuiltPath,
        long length,
        CancellationToken cancellationToken)
    {
        byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] rebuiltBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using FileStream source = File.OpenRead(sourcePath);
            await using FileStream rebuilt = File.OpenRead(rebuiltPath);
            long remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(BufferSize, remaining);
                await source.ReadExactlyAsync(sourceBuffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                await rebuilt.ReadExactlyAsync(rebuiltBuffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                if (!sourceBuffer.AsSpan(0, count).SequenceEqual(rebuiltBuffer.AsSpan(0, count)))
                {
                    return false;
                }

                remaining -= count;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer, true);
            ArrayPool<byte>.Shared.Return(rebuiltBuffer, true);
        }
    }

    private static void ValidateDestination(ArchivePair source, ArchivePair destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string[] sourcePaths = [source.FatPath, source.DatPath];
        string[] destinationPaths = [destination.FatPath, destination.DatPath];
        if (destinationPaths.Any(destinationPath =>
                sourcePaths.Any(sourcePath => string.Equals(sourcePath, destinationPath, comparison))))
        {
            throw new ArgumentException("Source and destination archive paths must be different.");
        }

        if (destinationPaths.Any(File.Exists))
        {
            throw new IOException("Destination FAT/DAT files must not already exist.");
        }

        if (destinationPaths.Any(path => !Directory.Exists(Path.GetDirectoryName(path))))
        {
            throw new DirectoryNotFoundException("Destination FAT/DAT directory does not exist.");
        }
    }

    private static string NormalizeSha256(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length != SHA256.HashSizeInBytes * 2 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal digits.", paramName);
        }

        return value.ToUpperInvariant();
    }
}
