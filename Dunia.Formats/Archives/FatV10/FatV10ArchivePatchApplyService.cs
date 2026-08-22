using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchivePatchApplyService
{
    public static Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            target,
            replacements,
            stagingStore,
            static (_, _) => Task.CompletedTask,
            cancellationToken);

    public static async Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        ReplacementStagingStore stagingStore,
        Func<ArchivePair, CancellationToken, Task> validatePublishedAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(stagingStore);
        ArgumentNullException.ThrowIfNull(validatePublishedAsync);
        cancellationToken.ThrowIfCancellationRequested();

        string token = Guid.NewGuid().ToString("N");
        var builtPair = new ArchivePair(
            $"{target.FatPath}.apply-{token}.fat",
            $"{target.DatPath}.apply-{token}.dat");
        string rollbackFatPath = $"{target.FatPath}.rollback-{token}.tmp";
        string rollbackDatPath = $"{target.DatPath}.rollback-{token}.tmp";
        bool publicationSucceeded = false;

        try
        {
            FatV10ArchivePatchFileBuildResult fileBuild = await FatV10ArchivePatchFileBuilder.BuildAsync(
                target,
                builtPair,
                replacements,
                stagingStore,
                cancellationToken).ConfigureAwait(false);
            ArchivePairBackupResult backup = await ArchivePairBackupService.EnsureCreatedAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await PublishWithRollbackAsync(
                target,
                builtPair,
                rollbackFatPath,
                rollbackDatPath,
                fileBuild.Build,
                validatePublishedAsync,
                cancellationToken).ConfigureAwait(false);
            publicationSucceeded = true;
            return new(backup, fileBuild.Build);
        }
        finally
        {
            File.Delete(builtPair.FatPath);
            File.Delete(builtPair.DatPath);
            if (publicationSucceeded)
            {
                File.Delete(rollbackFatPath);
                File.Delete(rollbackDatPath);
            }
        }
    }

    internal static async Task PublishWithRollbackAsync(
        ArchivePair target,
        ArchivePair built,
        string rollbackFatPath,
        string rollbackDatPath,
        FatV10ArchivePatchBuildResult expected,
        Func<ArchivePair, CancellationToken, Task> validatePublishedAsync,
        CancellationToken cancellationToken)
    {
        bool originalFatMoved = false;
        bool originalDatMoved = false;
        bool builtDatMoved = false;
        bool builtFatMoved = false;

        try
        {
            File.Move(target.FatPath, rollbackFatPath, false);
            originalFatMoved = true;
            File.Move(target.DatPath, rollbackDatPath, false);
            originalDatMoved = true;
            File.Move(built.DatPath, target.DatPath, false);
            builtDatMoved = true;
            File.Move(built.FatPath, target.FatPath, false);
            builtFatMoved = true;
            ValidatePublishedPair(target, expected);
            await validatePublishedAsync(target, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception publicationError)
        {
            try
            {
                if (builtFatMoved)
                {
                    File.Delete(target.FatPath);
                }

                if (builtDatMoved)
                {
                    File.Delete(target.DatPath);
                }

                if (originalDatMoved)
                {
                    File.Move(rollbackDatPath, target.DatPath, false);
                }

                if (originalFatMoved)
                {
                    File.Move(rollbackFatPath, target.FatPath, false);
                }
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Archive publication failed and rollback could not be completed.",
                    publicationError,
                    rollbackError);
            }

            throw;
        }
    }

    private static void ValidatePublishedPair(
        ArchivePair pair,
        FatV10ArchivePatchBuildResult expected)
    {
        long datLength = new FileInfo(pair.DatPath).Length;
        using FileStream fat = File.OpenRead(pair.FatPath);
        FatV10Index actual = FatV10IndexReader.Read(fat, datLength);
        if (!actual.Entries.SequenceEqual(expected.Index.Entries) || datLength != expected.DataLength)
        {
            throw new InvalidDataException("Published FAT/DAT pair failed validation.");
        }
    }
}
