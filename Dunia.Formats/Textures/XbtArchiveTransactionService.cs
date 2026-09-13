using System.Buffers.Binary;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Textures;

public static class XbtArchiveTransactionService
{
    public static async Task<XbtArchiveTransactionPlan> PlanAsync(
        ArchivePair source,
        IReadOnlyCollection<XbtArchiveTransactionItem> items,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(stagingStore);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one texture replacement is required.", nameof(items));
        }

        XbtArchiveTransactionItem[] ordered = items.OrderBy(item => item.EntryIndex).ToArray();
        if (ordered.Select(item => item.EntryIndex).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("Texture replacement entry indexes must be unique.", nameof(items));
        }

        var plans = new XbtArchiveReplacementPlan[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            XbtArchiveTransactionItem item = ordered[index];
            using var replacement = new MemoryStream();
            await stagingStore.CopyVerifiedToAsync(item.Replacement, replacement, cancellationToken).ConfigureAwait(false);
            replacement.Position = 0;
            plans[index] = await XbtArchiveReplacementService.PlanAsync(
                source,
                item.EntryIndex,
                item.ResourceNameHash,
                replacement,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    plans[index].SourcePayloadSha256,
                    item.ExpectedSourcePayloadSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    plans[index].ReplacementPayloadSha256,
                    item.ExpectedReplacementPayloadSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Staged texture replacement {item.EntryIndex} no longer matches its verified plan.");
            }
        }

        byte[] binding = new byte[checked(sizeof(int) + plans.Length * (sizeof(int) + 32))];
        BinaryPrimitives.WriteInt32LittleEndian(binding, plans.Length);
        for (int index = 0; index < plans.Length; index++)
        {
            int offset = sizeof(int) + index * (sizeof(int) + 32);
            BinaryPrimitives.WriteInt32LittleEndian(binding.AsSpan(offset), plans[index].EntryIndex);
            Convert.FromHexString(plans[index].PlanSha256).CopyTo(binding, offset + sizeof(int));
        }

        return new(Array.AsReadOnly(plans), Convert.ToHexString(SHA256.HashData(binding)));
    }

    public static async Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        string expectedPlanSha256,
        IReadOnlyCollection<XbtArchiveTransactionItem> items,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPlanSha256);
        XbtArchiveTransactionPlan plan = await PlanAsync(target, items, stagingStore, cancellationToken)
            .ConfigureAwait(false);
        RequirePlan(expectedPlanSha256, plan.PlanSha256);

        Dictionary<int, StagedReplacement> replacements = items.ToDictionary(
            item => item.EntryIndex,
            item => item.Replacement);

        return await FatV10ArchivePatchApplyService.ApplyAsync(
            target,
            replacements,
            stagingStore,
            async (pair, token) =>
            {
                XbtArchiveTransactionPlan lockedPlan = await PlanAsync(pair, items, stagingStore, token)
                    .ConfigureAwait(false);
                RequirePlan(expectedPlanSha256, lockedPlan.PlanSha256);
            },
            (pair, token) => ValidatePublishedTexturesAsync(pair, replacements.Keys, token),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidatePublishedTexturesAsync(
        ArchivePair pair,
        IEnumerable<int> entryIndexes,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(pair.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
        }

        await using FileStream data = File.OpenRead(pair.DatPath);
        foreach (int entryIndex in entryIndexes.Order())
        {
            using var xbt = new MemoryStream(index.Entries[entryIndex].UncompressedSize);
            await FatV10PayloadExtractor.ExtractAsync(data, index.Entries[entryIndex], xbt, cancellationToken)
                .ConfigureAwait(false);
            xbt.Position = 0;
            using var png = new MemoryStream();
            await XbtPngExporter.ExportAsync(xbt, png, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RequirePlan(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"XBT transaction plan mismatch. Expected {expected}, got {actual}.");
        }
    }
}
