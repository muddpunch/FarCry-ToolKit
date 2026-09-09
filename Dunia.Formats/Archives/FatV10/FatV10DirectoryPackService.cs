using Dunia.Formats.Archives;
using Dunia.Formats.Changes;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10DirectoryPackService
{
    public static async Task<FatV10DirectoryPackResult> BuildAsync(
        ArchivePair source,
        ArchivePair destination,
        string inputDirectory,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        string root = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Pack input directory was not found: {root}");
        }

        FatV10Index index;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(source.DatPath).Length);
        }

        Dictionary<ulong, int[]> entriesByHash = index.Entries
            .Select((entry, entryIndex) => (entry.NameHash, EntryIndex: entryIndex))
            .GroupBy(item => item.NameHash)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.EntryIndex).ToArray());
        string[] files = Directory.GetFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
        });
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        if (files.Length == 0)
        {
            throw new InvalidDataException("Pack input directory contains no files.");
        }

        using var store = new ReplacementStagingStore(stagingRoot);
        var replacements = new Dictionary<int, StagedReplacement>();
        var items = new List<FatV10DirectoryPackItem>(files.Length);
        var inputHashes = new Dictionary<ulong, string>();
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resourcePath = DuniaPathHash.Normalize(Path.GetRelativePath(root, file));
            ulong hash = DuniaCrc64.Compute(resourcePath);
            if (!inputHashes.TryAdd(hash, resourcePath))
            {
                throw new InvalidDataException(
                    $"Pack inputs '{inputHashes[hash]}' and '{resourcePath}' have the same resource hash {hash:X16}.");
            }

            if (!entriesByHash.TryGetValue(hash, out int[]? entryIndices))
            {
                throw new InvalidDataException(
                    $"Pack input '{resourcePath}' ({hash:X16}) does not match an archive entry.");
            }

            StagedReplacement staged = await store.StageAsync(file, cancellationToken).ConfigureAwait(false);
            foreach (int entryIndex in entryIndices)
            {
                replacements.Add(entryIndex, staged);
            }

            items.Add(new(
                resourcePath,
                hash,
                Array.AsReadOnly(entryIndices),
                staged.Length,
                staged.Sha256));
        }

        FatV10ArchivePatchFileBuildResult build = await FatV10ArchivePatchFileBuilder.BuildAsync(
            source,
            destination,
            replacements,
            store,
            (pair, token) => ValidateTargetsAsync(pair, replacements.Keys, index, token),
            cancellationToken).ConfigureAwait(false);
        try
        {
            await FatV10PublishedReplacementValidator.ValidateAsync(
                destination, replacements, cancellationToken).ConfigureAwait(false);
            return new(build, items.AsReadOnly());
        }
        catch
        {
            File.Delete(destination.FatPath);
            File.Delete(destination.DatPath);
            throw;
        }
    }

    private static Task ValidateTargetsAsync(
        ArchivePair source,
        IEnumerable<int> replacementIndices,
        FatV10Index plannedIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FatV10Index current;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            current = FatV10IndexReader.Read(fat, new FileInfo(source.DatPath).Length);
        }

        if (current.Entries.Count != plannedIndex.Entries.Count || replacementIndices.Any(index =>
                current.Entries[index].NameHash != plannedIndex.Entries[index].NameHash))
        {
            throw new InvalidDataException("Source archive targets changed while preparing the pack operation.");
        }

        return Task.CompletedTask;
    }
}
