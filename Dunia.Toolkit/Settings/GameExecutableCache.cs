using System.IO;

namespace Dunia.Toolkit.Settings;

internal static class GameExecutableCache
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DuniaToolkit",
        "game-executable.cache");

    public static async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            string path = (await File.ReadAllTextAsync(CachePath, cancellationToken)).Trim();
            return IsValid(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(executablePath);
        if (!IsValid(fullPath))
        {
            throw new ArgumentException("Select the original FarCry5.exe file.", nameof(executablePath));
        }

        string directory = Path.GetDirectoryName(CachePath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $"{Path.GetFileName(CachePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, fullPath, cancellationToken);
            File.Move(temporaryPath, CachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool IsValid(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetFileName(path), "FarCry5.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);
}
