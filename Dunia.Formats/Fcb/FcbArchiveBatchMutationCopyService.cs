using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveBatchMutationCopyService
{
    public static async Task<FcbArchiveBatchMutationCopyResult> CreateAsync(
        ArchivePair source,
        ArchivePair destination,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveFieldMutation> mutations,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        ValidateDestination(source, destination);

        FcbArchiveBatchMutationArtifact artifact = await
            FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                source,
                entryIndex,
                expectedResourceNameHash,
                schema,
                mutations,
                cancellationToken).ConfigureAwait(false);
        await FcbArchiveBatchMutationDryRunService.RunAsync(
            source,
            artifact,
            temporaryRoot,
            cancellationToken).ConfigureAwait(false);

        bool published = false;
        try
        {
            using var store = new ReplacementStagingStore(Path.GetFullPath(temporaryRoot));
            using var replacementInput = new MemoryStream(artifact.PlannedPayload, false);
            StagedReplacement replacement = await store.StageAsync(
                replacementInput,
                $"entry-{entryIndex}.fcb",
                cancellationToken).ConfigureAwait(false);
            FatV10ArchivePatchFileBuildResult build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                source,
                destination,
                new Dictionary<int, StagedReplacement> { [entryIndex] = replacement },
                store,
                async (lockedSource, token) =>
                {
                    FcbArchiveBatchMutationArtifact locked = await
                        FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                            lockedSource,
                            entryIndex,
                            expectedResourceNameHash,
                            schema,
                            mutations,
                            token).ConfigureAwait(false);
                    if (!string.Equals(
                            locked.Plan.SourcePayloadSha256,
                            artifact.Plan.SourcePayloadSha256,
                            StringComparison.Ordinal) ||
                        !locked.PlannedPayload.AsSpan().SequenceEqual(artifact.PlannedPayload))
                    {
                        throw new InvalidDataException(
                            "Locked FCB batch mutation differs from the verified copy plan.");
                    }
                },
                cancellationToken).ConfigureAwait(false);
            published = true;

            FatV10Entry outputEntry = build.Build.Index.Entries[entryIndex];
            byte[] outputPayload;
            await using (FileStream data = File.OpenRead(destination.DatPath))
            await using (var payload = new MemoryStream(artifact.PlannedPayload.Length))
            {
                await FatV10PayloadExtractor.ExtractAsync(
                    data,
                    outputEntry,
                    payload,
                    cancellationToken).ConfigureAwait(false);
                outputPayload = payload.ToArray();
            }

            bool payloadExact = artifact.PlannedPayload.AsSpan().SequenceEqual(outputPayload);
            if (!payloadExact)
            {
                throw new InvalidDataException(
                    "Published archive copy contains an unexpected FCB batch payload.");
            }

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

    private static void ValidateDestination(ArchivePair source, ArchivePair destination)
    {
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
}
