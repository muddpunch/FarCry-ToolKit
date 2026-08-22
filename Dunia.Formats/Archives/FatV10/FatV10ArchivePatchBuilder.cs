using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchivePatchBuilder
{
    private const int Alignment = 16;
    private const int BufferSize = 1024 * 1024;

    public static async Task<FatV10ArchivePatchBuildResult> BuildAsync(
        Stream fatInput,
        Stream datInput,
        Stream fatOutput,
        Stream datOutput,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        ReplacementStagingStore stagingStore,
        CancellationToken cancellationToken = default)
    {
        ValidateStreams(fatInput, datInput, fatOutput, datOutput);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(stagingStore);
        cancellationToken.ThrowIfCancellationRequested();

        FatV10Index sourceIndex = FatV10IndexReader.Read(fatInput, datInput.Length);
        ValidateReplacements(sourceIndex, replacements);
        var entries = sourceIndex.Entries.ToArray();
        long originalDatPosition = datInput.Position;

        try
        {
            datInput.Position = 0;
            await datInput.CopyToAsync(datOutput, BufferSize, cancellationToken).ConfigureAwait(false);

            foreach ((int index, StagedReplacement replacement) in replacements.OrderBy(item => item.Key))
            {
                await AlignAsync(datOutput, cancellationToken).ConfigureAwait(false);
                long offset = datOutput.Position;
                await stagingStore.CopyVerifiedToAsync(replacement, datOutput, cancellationToken)
                    .ConfigureAwait(false);

                entries[index] = entries[index] with
                {
                    UncompressedSize = checked((int)replacement.Length),
                    Offset = offset,
                    StoredSize = checked((int)replacement.Length),
                    CompressionScheme = FatV10CompressionScheme.None,
                    IsEncrypted = false,
                };
            }

            FatV10IndexWriter.Write(fatOutput, entries);
            var summary = new FatV10IndexSummary(
                1,
                entries.Length,
                FatV10IndexSummaryReader.EntrySize,
                fatOutput.Position);
            return new(new(summary, Array.AsReadOnly(entries)), datOutput.Position, replacements.Count);
        }
        catch
        {
            fatOutput.SetLength(0);
            fatOutput.Position = 0;
            datOutput.SetLength(0);
            datOutput.Position = 0;
            throw;
        }
        finally
        {
            datInput.Position = originalDatPosition;
        }
    }

    private static void ValidateStreams(
        Stream fatInput,
        Stream datInput,
        Stream fatOutput,
        Stream datOutput)
    {
        ArgumentNullException.ThrowIfNull(fatInput);
        ArgumentNullException.ThrowIfNull(datInput);
        ArgumentNullException.ThrowIfNull(fatOutput);
        ArgumentNullException.ThrowIfNull(datOutput);

        if (!fatInput.CanRead || !fatInput.CanSeek)
        {
            throw new ArgumentException("FAT input must be readable and seekable.", nameof(fatInput));
        }

        if (!datInput.CanRead || !datInput.CanSeek)
        {
            throw new ArgumentException("DAT input must be readable and seekable.", nameof(datInput));
        }

        ValidateOutput(fatOutput, nameof(fatOutput));
        ValidateOutput(datOutput, nameof(datOutput));

        if (ReferenceEquals(fatInput, fatOutput) ||
            ReferenceEquals(fatInput, datOutput) ||
            ReferenceEquals(datInput, fatOutput) ||
            ReferenceEquals(datInput, datOutput) ||
            ReferenceEquals(fatOutput, datOutput))
        {
            throw new ArgumentException("Archive build streams must be distinct.");
        }
    }

    private static void ValidateOutput(Stream output, string paramName)
    {
        if (!output.CanWrite || !output.CanSeek)
        {
            throw new ArgumentException("Archive output must be writable and seekable.", paramName);
        }

        if (output.Length != 0 || output.Position != 0)
        {
            throw new ArgumentException("Archive output must be empty and positioned at zero.", paramName);
        }
    }

    private static void ValidateReplacements(
        FatV10Index index,
        IReadOnlyDictionary<int, StagedReplacement> replacements)
    {
        foreach ((int entryIndex, StagedReplacement replacement) in replacements)
        {
            if ((uint)entryIndex >= (uint)index.Entries.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replacements),
                    entryIndex,
                    $"Replacement entry index must be between 0 and {index.Entries.Count - 1}.");
            }

            if (replacement is null)
            {
                throw new ArgumentException(
                    $"Replacement for entry {entryIndex} cannot be null.",
                    nameof(replacements));
            }

            if (replacement.Length > FatV10EntryWriter.MaxStoredSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replacements),
                    $"Replacement for entry {entryIndex} exceeds the FAT v10 stored-size limit.");
            }
        }
    }

    private static async Task AlignAsync(Stream output, CancellationToken cancellationToken)
    {
        int padding = (int)((Alignment - (output.Position & (Alignment - 1))) & (Alignment - 1));
        if (padding > 0)
        {
            await output.WriteAsync(new byte[padding], cancellationToken).ConfigureAwait(false);
        }
    }
}
