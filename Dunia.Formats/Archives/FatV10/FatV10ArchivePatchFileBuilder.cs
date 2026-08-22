using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchivePatchFileBuilder
{
    private const int BufferSize = 1024 * 1024;

    public static Task<FatV10ArchivePatchFileBuildResult> BuildAsync(
        ArchivePair source,
        ArchivePair destination,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default) =>
        BuildAsync(
            source,
            destination,
            replacements,
            stagingStore,
            static (_, _) => Task.CompletedTask,
            cancellationToken);

    internal static async Task<FatV10ArchivePatchFileBuildResult> BuildAsync(
        ArchivePair source,
        ArchivePair destination,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        ReplacementStagingStore stagingStore,
        Func<ArchivePair, CancellationToken, Task> validateSourceAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(stagingStore);
        ArgumentNullException.ThrowIfNull(validateSourceAsync);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePaths(source, destination);

        string token = Guid.NewGuid().ToString("N");
        string temporaryFatPath = $"{destination.FatPath}.{token}.tmp";
        string temporaryDatPath = $"{destination.DatPath}.{token}.tmp";
        bool publishedDat = false;

        try
        {
            FatV10ArchivePatchBuildResult build;
            await using (FileStream fatInput = OpenInput(source.FatPath))
            await using (FileStream datInput = OpenInput(source.DatPath))
            await using (FileStream fatOutput = OpenOutput(temporaryFatPath))
            await using (FileStream datOutput = OpenOutput(temporaryDatPath))
            {
                await validateSourceAsync(source, cancellationToken).ConfigureAwait(false);
                build = await FatV10ArchivePatchBuilder.BuildAsync(
                    fatInput,
                    datInput,
                    fatOutput,
                    datOutput,
                    replacements,
                    stagingStore,
                    cancellationToken).ConfigureAwait(false);

                await datOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
                datOutput.Flush(true);
                await fatOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
                fatOutput.Flush(true);
            }

            ValidateBuiltPair(temporaryFatPath, temporaryDatPath, build);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporaryDatPath, destination.DatPath, false);
            publishedDat = true;
            try
            {
                File.Move(temporaryFatPath, destination.FatPath, false);
            }
            catch
            {
                File.Delete(destination.DatPath);
                publishedDat = false;
                throw;
            }

            return new(destination, build);
        }
        finally
        {
            File.Delete(temporaryFatPath);
            File.Delete(temporaryDatPath);
            if (publishedDat && !File.Exists(destination.FatPath))
            {
                File.Delete(destination.DatPath);
            }
        }
    }

    private static void ValidatePaths(ArchivePair source, ArchivePair destination)
    {
        EnsureExists(source.FatPath);
        EnsureExists(source.DatPath);
        EnsureDistinct(source.FatPath, source.DatPath, "Source FAT and DAT paths must be different.");
        EnsureDistinct(destination.FatPath, destination.DatPath, "Destination FAT and DAT paths must be different.");

        foreach (string sourcePath in new[] { source.FatPath, source.DatPath })
        {
            foreach (string destinationPath in new[] { destination.FatPath, destination.DatPath })
            {
                EnsureDistinct(sourcePath, destinationPath, "Source and destination archive paths must be different.");
            }
        }

        if (File.Exists(destination.FatPath) || File.Exists(destination.DatPath))
        {
            throw new IOException("Destination FAT/DAT files must not already exist.");
        }

        EnsureParentExists(destination.FatPath);
        EnsureParentExists(destination.DatPath);
    }

    private static void ValidateBuiltPair(
        string fatPath,
        string datPath,
        FatV10ArchivePatchBuildResult expected)
    {
        long datLength = new FileInfo(datPath).Length;
        using FileStream fat = File.OpenRead(fatPath);
        FatV10Index index = FatV10IndexReader.Read(fat, datLength);
        if (!index.Entries.SequenceEqual(expected.Index.Entries) || datLength != expected.DataLength)
        {
            throw new InvalidDataException("Rebuilt FAT/DAT pair failed publication validation.");
        }
    }

    private static FileStream OpenInput(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static FileStream OpenOutput(string path) => new(
        path,
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void EnsureExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Archive source file was not found.", path);
        }
    }

    private static void EnsureDistinct(string left, string right, string message)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(left, right, comparison))
        {
            throw new ArgumentException(message);
        }
    }

    private static void EnsureParentExists(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException($"Destination directory was not found: {parent}");
        }
    }
}
