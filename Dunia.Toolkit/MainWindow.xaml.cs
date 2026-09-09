using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Hashing;
using Dunia.Formats.Meshes;
using Dunia.Formats.Textures;
using Dunia.Toolkit.Browser;
using Dunia.Toolkit.Settings;
using Microsoft.Win32;

namespace Dunia.Toolkit;

public partial class MainWindow : Window, IDisposable
{
    private const int PageSize = 5_000;

    private readonly DispatcherTimer _filterTimer;
    private FatV10Index? _index;
    private DuniaNameResolver? _resolver;
    private string? _nameCatalogPath;
    private string? _fatPath;
    private string? _datPath;
    private string? _gameExecutablePath;
    private CancellationTokenSource? _operationCancellation;
    private int _pageIndex;
    private int _pageCount = 1;
    private int _pageRequestVersion;
    private bool _isBusy;
    private bool _isDisposed;

    public MainWindow()
    {
        InitializeComponent();
        _filterTimer = new(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, ApplyFilter, Dispatcher);
        _filterTimer.Stop();
        PreviewKeyDown += WindowPreviewKeyDown;
        Loaded += WindowLoaded;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        string? cachedPath = await GameExecutableCache.LoadAsync();
        if (cachedPath is not null)
        {
            SetGameExecutable(cachedPath, "FarCry5.exe loaded from cache");
            return;
        }

        await SelectGameExecutableAsync();
    }

    private async void OpenArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !await EnsureGameExecutableAsync())
        {
            return;
        }

        string gameDirectory = Path.GetDirectoryName(_gameExecutablePath!)!;

        var dialog = new OpenFileDialog
        {
            Title = "Open Far Cry 5 archive index",
            Filter = "Dunia archive index (*.fat)|*.fat|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetDefaultArchiveDirectory(gameDirectory),
        };

        if (dialog.ShowDialog(this) == true)
        {
            await LoadArchiveAsync(dialog.FileName);
        }
    }

    private async void SelectGameExecutableClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await SelectGameExecutableAsync();
        }
    }

    private async Task<bool> EnsureGameExecutableAsync()
    {
        if (GameExecutableCache.IsValid(_gameExecutablePath))
        {
            return true;
        }

        string? cachedPath = await GameExecutableCache.LoadAsync();
        if (cachedPath is not null)
        {
            SetGameExecutable(cachedPath, "FarCry5.exe loaded from cache");
            return true;
        }

        return await SelectGameExecutableAsync();
    }

    private async Task<bool> SelectGameExecutableAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select FarCry5.exe",
            Filter = "Far Cry 5 executable (FarCry5.exe)|FarCry5.exe|Executable files (*.exe)|*.exe",
            FileName = "FarCry5.exe",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = _gameExecutablePath is null ? null : Path.GetDirectoryName(_gameExecutablePath),
        };

        if (dialog.ShowDialog(this) != true)
        {
            if (GameExecutableCache.IsValid(_gameExecutablePath))
            {
                SetGameExecutable(_gameExecutablePath!, "FarCry5.exe selection unchanged");
                return true;
            }

            GameExecutableText.Text = "required";
            GameExecutableText.ToolTip = "Select FarCry5.exe before opening an archive.";
            StatusText.Text = "FarCry5.exe is required before opening an archive";
            return false;
        }

        if (!GameExecutableCache.IsValid(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "Select the original file named FarCry5.exe.",
                "Invalid game executable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(dialog.FileName);
            await GameExecutableCache.SaveAsync(fullPath);
            SetGameExecutable(fullPath, "FarCry5.exe selected and saved in cache");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Cannot save game executable", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SetGameExecutable(string path, string status)
    {
        _gameExecutablePath = path;
        GameExecutableText.Text = "selected";
        GameExecutableText.ToolTip = path;
        StatusText.Text = status;
    }

    private async Task LoadArchiveAsync(string fatPath)
    {
        _pageRequestVersion++;
        string fullFatPath = Path.GetFullPath(fatPath);
        string datPath = Path.ChangeExtension(fullFatPath, ".dat");

        if (!File.Exists(datPath))
        {
            ShowError($"Paired DAT file not found: {datPath}");
            return;
        }

        SetBusy(true, "Reading archive...");
        try
        {
            DuniaNameResolver? resolver = _resolver;
            string? catalogPath = _nameCatalogPath;
            string[] automaticCatalogs = FindAutomaticNameCatalogs(fullFatPath);

            if (automaticCatalogs.Length > 0)
            {
                resolver = await MergeResolversAsync(resolver, automaticCatalogs);
                catalogPath = string.Join(Environment.NewLine, automaticCatalogs);
            }

            long datLength = new FileInfo(datPath).Length;
            ArchiveLoadResult result = await Task.Run(() =>
            {
                using FileStream fat = File.Open(fullFatPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                FatV10Index index = FatV10IndexReader.Read(fat, datLength);
                ArchiveEntryPage page = CreatePage(index, resolver, string.Empty, 0);
                return new ArchiveLoadResult(index, page, CountResolved(index, resolver));
            });

            _index = result.Index;
            _resolver = resolver;
            _nameCatalogPath = catalogPath;
            _fatPath = fullFatPath;
            _datPath = datPath;
            _pageIndex = 0;
            BindPage(result.Page);

            ArchiveTitle.Text = Path.GetFileNameWithoutExtension(fullFatPath);
            ArchivePathText.Text = fullFatPath;
            ArchivePathText.ToolTip = fullFatPath;
            EntryCountText.Text = result.Index.Entries.Count.ToString("N0", CultureInfo.CurrentCulture);
            DatSizeText.Text = FormatBytes(datLength);
            FormatText.Text = $"FAT v{FatV10IndexSummaryReader.Version}";
            GameExecutableText.Text = "selected";
            GameExecutableText.ToolTip = _gameExecutablePath;
            UpdateNameCoverage(result.ResolvedCount, result.Index.Entries.Count);
            EmptyState.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Loaded {result.Index.Entries.Count:N0} entries";
            SearchBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError(ex is OutOfMemoryException
                ? "The archive is too large for the available memory. Close other applications and try again."
                : ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoadNamesClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Load resource name list",
            Filter = "Name lists (*.filelist;*.list;*.txt)|*.filelist;*.list;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true, "Loading name list...");
        try
        {
            DuniaNameResolver resolver = await MergeResolversAsync(_resolver, [dialog.FileName]);
            _pageIndex = 0;
            ArchiveEntryPage? page = _index is null
                ? null
                : await Task.Run(() => CreatePage(_index, resolver, SearchBox.Text.Trim(), _pageIndex));

            _resolver = resolver;
            _nameCatalogPath = Path.GetFullPath(dialog.FileName);

            if (_index is not null)
            {
                BindPage(page!);
                UpdateNameCoverage(CountResolved(_index, resolver), _index.Entries.Count);
            }
            else
            {
                NameCoverageText.Text = $"{resolver.NameCount:N0} loaded";
            }

            StatusText.Text = $"Loaded {resolver.NameCount:N0} candidate names from {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static Task<DuniaNameResolver> MergeResolversAsync(
        DuniaNameResolver? resolver,
        IReadOnlyList<string> paths) => Task.Run(() =>
    {
        resolver ??= new DuniaNameResolver();
        foreach (string path in paths)
        {
            using var input = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = input.ReadLine()) is not null)
            {
                string candidate = line.Trim();
                if (candidate.Length > 0 && !candidate.StartsWith('#') && !candidate.StartsWith(';'))
                {
                    resolver.Add(candidate);
                }
            }
        }

        return resolver;
    });

    private async void DiscoverNamesClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _index is null || _fatPath is null || _datPath is null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            _gameExecutablePath is null
                ? "The toolkit will scan decoded archive payloads for embedded paths. FarCry5.exe was not found. This is read-only, but a large DAT can take several minutes."
                : "The toolkit will scan decoded archive payloads and FarCry5.exe for embedded paths. This is read-only, but large files can take several minutes.",
            "Discover resource names",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _operationCancellation.Token;
        CancelButton.Visibility = Visibility.Visible;
        SetBusy(true, "Starting name discovery...");

        try
        {
            var progress = new Progress<FatV10ArchiveNameDiscoveryProgress>(value =>
            {
                double percent = value.TotalEntryCount == 0
                    ? 100
                    : (double)value.ProcessedEntryCount / value.TotalEntryCount * 100;
                StatusText.Text = $"Discovering names: {percent:0.0}% — {value.MatchCount:N0} matches";
            });

            await using var data = new FileStream(_datPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            FatV10ArchiveNameDiscoveryResult result = await FatV10ArchiveNameDiscovery.DiscoverAsync(
                data,
                _index.Entries,
                _index.Entries.Select(entry => entry.NameHash),
                progress,
                cancellationToken: cancellationToken);

            var discoveredNames = new HashSet<string>(StringComparer.Ordinal);
            DuniaNameResolver resolver = _resolver ?? new DuniaNameResolver();
            foreach (FatV10ArchiveNameDiscoveryMatch match in result.Matches)
            {
                if (discoveredNames.Add(match.Name))
                {
                    resolver.Add(match.Name);
                }
            }

            if (_gameExecutablePath is not null)
            {
                var executableProgress = new Progress<DuniaPathNameDiscoveryProgress>(value =>
                {
                    double percent = value.TotalBytes <= 0
                        ? 0
                        : (double)value.ProcessedBytes / value.TotalBytes * 100;
                    StatusText.Text = $"Scanning FarCry5.exe: {percent:0.0}% - {value.MatchCount:N0} matches";
                });

                await using var executable = new FileStream(_gameExecutablePath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
                DuniaPathNameDiscoveryResult executableResult = await DuniaPathNameDiscovery.DiscoverAsync(
                    executable,
                    _index.Entries.Select(entry => entry.NameHash),
                    executableProgress,
                    cancellationToken);

                foreach (DuniaPathNameDiscoveryMatch match in executableResult.Matches)
                {
                    if (discoveredNames.Add(match.Name))
                    {
                        resolver.Add(match.Name);
                    }
                }
            }

            string cachePath = GetNameCachePath(_fatPath);
            int cachedNameCount = await DuniaNameCatalogStore.MergeAsync(
                cachePath,
                discoveredNames,
                cancellationToken);
            _pageIndex = 0;
            ArchiveEntryPage page = await Task.Run(
                () => CreatePage(_index, resolver, SearchBox.Text.Trim(), _pageIndex),
                cancellationToken);
            _resolver = resolver;
            _nameCatalogPath = cachePath;
            BindPage(page);
            UpdateNameCoverage(CountResolved(_index, resolver), _index.Entries.Count);
            StatusText.Text = $"Discovered {discoveredNames.Count:N0} names; {cachedNameCount:N0} saved in cache";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Name discovery cancelled";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            CancelButton.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private static ArchiveEntryPage CreatePage(
        FatV10Index index,
        DuniaNameResolver? resolver,
        string filter,
        int pageIndex)
    {
        int requestedStart = checked(pageIndex * PageSize);
        var rows = new List<ArchiveEntryRow>(PageSize);

        if (filter.Length == 0)
        {
            int total = index.Entries.Count;
            int actualPage = total == 0 ? 0 : Math.Min(pageIndex, (total - 1) / PageSize);
            int start = actualPage * PageSize;
            int end = Math.Min(start + PageSize, total);
            for (int i = start; i < end; i++)
            {
                rows.Add(CreateRow(i, index.Entries[i], resolver));
            }

            return new(rows.ToArray(), total, actualPage);
        }

        int matchCount = 0;
        for (int i = 0; i < index.Entries.Count; i++)
        {
            FatV10Entry entry = index.Entries[i];
            string? resolvedName = RenderName(resolver?.Resolve(entry.NameHash) ?? []);
            if (!MatchesFilter(i, entry, resolvedName, filter))
            {
                continue;
            }

            if (matchCount >= requestedStart && rows.Count < PageSize)
            {
                rows.Add(new(i, entry, resolvedName));
            }

            matchCount++;
        }

        return new(rows.ToArray(), matchCount, pageIndex);
    }

    private static ArchiveEntryRow CreateRow(int index, FatV10Entry entry, DuniaNameResolver? resolver) =>
        new(index, entry, RenderName(resolver?.Resolve(entry.NameHash) ?? []));

    private void BindPage(ArchiveEntryPage page)
    {
        _pageIndex = page.PageIndex;
        _pageCount = Math.Max(1, (page.MatchCount + PageSize - 1) / PageSize);
        EntriesGrid.ItemsSource = page.Rows;
        EntriesGrid.SelectedItem = null;
        PageText.Text = $"Page {_pageIndex + 1:N0} / {_pageCount:N0}";
        UpdatePageControls();
    }

    private void SearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private async void ApplyFilter(object? sender, EventArgs e)
    {
        _filterTimer.Stop();
        _pageIndex = 0;
        await RefreshPageAsync();
    }

    private async void PreviousPageClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy && _pageIndex > 0)
        {
            _pageIndex--;
            await RefreshPageAsync();
        }
    }

    private async void NextPageClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy && _pageIndex + 1 < _pageCount)
        {
            _pageIndex++;
            await RefreshPageAsync();
        }
    }

    private async Task RefreshPageAsync()
    {
        if (_index is null || _isBusy)
        {
            return;
        }

        int requestVersion = ++_pageRequestVersion;
        int requestedPage = _pageIndex;
        string filter = SearchBox.Text.Trim();
        FatV10Index index = _index;
        DuniaNameResolver? resolver = _resolver;
        StatusText.Text = filter.Length == 0 ? "Loading page..." : "Filtering archive...";
        PreviousPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;

        ArchiveEntryPage page = await Task.Run(() => CreatePage(index, resolver, filter, requestedPage));
        if (requestVersion != _pageRequestVersion || !ReferenceEquals(index, _index))
        {
            return;
        }

        BindPage(page);
        int first = page.MatchCount == 0 ? 0 : (page.PageIndex * PageSize) + 1;
        int last = page.MatchCount == 0 ? 0 : Math.Min(first + page.Rows.Length - 1, page.MatchCount);
        StatusText.Text = filter.Length == 0
            ? $"Showing entries {first:N0}-{last:N0} of {page.MatchCount:N0}"
            : $"Showing matches {first:N0}-{last:N0} of {page.MatchCount:N0}";
    }

    private static bool MatchesFilter(int index, FatV10Entry entry, string? resolvedName, string filter)
    {
        string name = resolvedName ?? $"Entry {index:D6}";
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.NameHash.ToString("X16", CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               index.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.CompressionScheme.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (entry.IsEncrypted ? "Encrypted" : "None").Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void EntrySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not ArchiveEntryRow entry)
        {
            InspectorPanel.Visibility = Visibility.Collapsed;
            InspectorEmpty.Visibility = Visibility.Visible;
            InspectMeshButton.Visibility = Visibility.Collapsed;
            PreviewTextureButton.Visibility = Visibility.Collapsed;
            EditFcbButton.Visibility = Visibility.Collapsed;
            MeshSummaryGroup.Visibility = Visibility.Collapsed;
            TexturePreviewGroup.Visibility = Visibility.Collapsed;
            TexturePreviewImage.Source = null;
            return;
        }

        InspectorEmpty.Visibility = Visibility.Collapsed;
        InspectorPanel.Visibility = Visibility.Visible;
        SelectedIndexText.Text = $"Entry {entry.Index:N0}";
        SelectedNameText.Text = entry.Name;
        SelectedHashText.Text = entry.Hash;
        SelectedOffsetText.Text = $"0x{entry.Offset:X16}";
        SelectedRawSizeText.Text = entry.RawSize;
        SelectedStoredSizeText.Text = entry.StoredSize;
        SelectedCompressionText.Text = entry.IsEncrypted ? $"{entry.Compression} / encrypted" : entry.Compression;
        InspectMeshButton.Visibility = entry.CanInspectMesh ? Visibility.Visible : Visibility.Collapsed;
        PreviewTextureButton.Visibility = entry.CanPreviewTexture ? Visibility.Visible : Visibility.Collapsed;
        int selectedFcbCount = EntriesGrid.SelectedItems.OfType<ArchiveEntryRow>().Count(row => row.CanEditFcb);
        EditFcbButton.Visibility = selectedFcbCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditFcbButton.Content = selectedFcbCount == 1
            ? "Edit selected FCB fields..."
            : $"Edit {selectedFcbCount:N0} selected FCB entries...";
        MeshSummaryGroup.Visibility = Visibility.Collapsed;
        TexturePreviewGroup.Visibility = Visibility.Collapsed;
        TexturePreviewImage.Source = null;
    }

    private async void InspectMeshClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy ||
            _datPath is null ||
            EntriesGrid.SelectedItem is not ArchiveEntryRow { CanInspectMesh: true } entry)
        {
            return;
        }

        const int maxPreviewSize = 256 * 1024 * 1024;
        if (entry.Entry.UncompressedSize > maxPreviewSize)
        {
            ShowError("Mesh exceeds the 256 MB preview safety limit.");
            return;
        }

        SetBusy(true, "Reading mesh...");
        try
        {
            XbgMeshPreview mesh;
            await using (var data = new FileStream(_datPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.RandomAccess,
            }))
            await using (var payload = new MemoryStream(entry.Entry.UncompressedSize))
            {
                await FatV10PayloadExtractor.ExtractAsync(data, entry.Entry, payload);
                payload.Position = 0;
                mesh = await Task.Run(() => XbgMeshPreviewReader.Read(payload));
            }

            XbgSummary summary = mesh.Summary;
            MeshVersionText.Text = $"0x{summary.Version:X8}";
            MeshLodCountText.Text = summary.LodCount.ToString("N0", CultureInfo.CurrentCulture);
            MeshMaterialCountText.Text = summary.MaterialCount.ToString("N0", CultureInfo.CurrentCulture);
            MeshChunkCountText.Text = summary.Chunks.Count.ToString("N0", CultureInfo.CurrentCulture);
            MeshSummaryGroup.Visibility = Visibility.Visible;
            StatusText.Text = $"Mesh decoded: {mesh.Positions.Count:N0} vertices, {mesh.TriangleCount:N0} triangles";
            var preview = new MeshPreviewWindow(mesh, entry.Name) { Owner = this };
            preview.Show();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PreviewTextureClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy ||
            _datPath is null ||
            EntriesGrid.SelectedItem is not ArchiveEntryRow { CanPreviewTexture: true } entry)
        {
            return;
        }

        const int maxPreviewSize = 128 * 1024 * 1024;
        if (entry.Entry.UncompressedSize > maxPreviewSize)
        {
            ShowError("Texture exceeds the 128 MB preview safety limit.");
            return;
        }

        SetBusy(true, "Reading texture...");
        try
        {
            BitmapSource bitmap;
            await using (var data = new FileStream(_datPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.RandomAccess,
            }))
            await using (var xbt = new MemoryStream(entry.Entry.UncompressedSize))
            await using (var dds = new MemoryStream())
            {
                await FatV10PayloadExtractor.ExtractAsync(data, entry.Entry, xbt);
                xbt.Position = 0;
                await XbtDdsExtractor.ExtractAsync(xbt, dds);
                dds.Position = 0;

                bitmap = await DdsBitmapDecoder.DecodeAsync(dds);
            }

            TexturePreviewImage.Source = bitmap;
            TextureDimensionsText.Text = $"{bitmap.PixelWidth:N0} x {bitmap.PixelHeight:N0} - {bitmap.Format}";
            TexturePreviewGroup.Visibility = Visibility.Visible;
            StatusText.Text = $"Texture decoded: {bitmap.PixelWidth:N0} x {bitmap.PixelHeight:N0}";
        }
        catch (Exception ex)
        {
            ShowError($"Texture preview failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ExportTexturePngClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || EntriesGrid.SelectedItem is not ArchiveEntryRow { CanPreviewTexture: true } entry)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export texture as PNG",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{Path.GetFileNameWithoutExtension(entry.Name)}.png",
            AddExtension = true,
            DefaultExt = ".png",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await WriteTextureOutputAsync(dialog.FileName, "Exporting PNG...", async (xbt, output, token) =>
        {
            await XbtPngExporter.ExportAsync(xbt, output, token);
        });
    }

    private async void CreateReplacementXbtClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || EntriesGrid.SelectedItem is not ArchiveEntryRow { CanPreviewTexture: true } entry)
        {
            return;
        }

        var inputDialog = new OpenFileDialog
        {
            Title = "Select replacement texture",
            Filter = "Supported textures (*.png;*.dds)|*.png;*.dds|PNG image (*.png)|*.png|DDS texture (*.dds)|*.dds",
            CheckFileExists = true,
        };
        if (inputDialog.ShowDialog(this) != true)
        {
            return;
        }

        var outputDialog = new SaveFileDialog
        {
            Title = "Create replacement XBT",
            Filter = "XBT texture (*.xbt)|*.xbt",
            FileName = $"{Path.GetFileNameWithoutExtension(entry.Name)}.replacement.xbt",
            AddExtension = true,
            DefaultExt = ".xbt",
            OverwritePrompt = true,
        };
        if (outputDialog.ShowDialog(this) != true)
        {
            return;
        }

        string inputPath = inputDialog.FileName;
        await WriteTextureOutputAsync(outputDialog.FileName, "Creating replacement XBT...", async (xbt, output, token) =>
        {
            await using FileStream replacement = File.OpenRead(inputPath);
            if (string.Equals(Path.GetExtension(inputPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                await XbtPngImporter.ImportAsync(xbt, replacement, output, token);
            }
            else
            {
                await XbtDdsImporter.ImportAsync(xbt, replacement, output, token);
            }
        });
    }

    private async Task WriteTextureOutputAsync(
        string outputPath,
        string status,
        Func<Stream, Stream, CancellationToken, Task> writeAsync)
    {
        if (_datPath is null || EntriesGrid.SelectedItem is not ArchiveEntryRow { CanPreviewTexture: true } entry)
        {
            return;
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        string temporaryPath = $"{fullOutputPath}.{Guid.NewGuid():N}.tmp";
        CancellationToken token = CancellationToken.None;
        SetBusy(true, status);
        try
        {
            if (File.Exists(fullOutputPath))
            {
                throw new IOException("Output file already exists.");
            }

            using var xbt = new MemoryStream(entry.Entry.UncompressedSize);
            await using (var data = File.OpenRead(_datPath))
            {
                await FatV10PayloadExtractor.ExtractAsync(data, entry.Entry, xbt, token);
            }

            xbt.Position = 0;
            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                80 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await writeAsync(xbt, output, token);
                await output.FlushAsync(token);
                output.Flush(true);
            }

            File.Move(temporaryPath, fullOutputPath, false);
            StatusText.Text = $"Created {Path.GetFileName(fullOutputPath)}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            File.Delete(temporaryPath);
            SetBusy(false);
        }
    }

    private async void ReplaceTextureClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy ||
            _fatPath is null ||
            _datPath is null ||
            EntriesGrid.SelectedItem is not ArchiveEntryRow { CanPreviewTexture: true } entry)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select replacement texture",
            Filter = "Supported textures (*.png;*.dds;*.xbt)|*.png;*.dds;*.xbt|PNG image (*.png)|*.png|DDS texture (*.dds)|*.dds|XBT texture (*.xbt)|*.xbt",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var source = new ArchivePair(_fatPath, _datPath);
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "DuniaToolkit");
        _operationCancellation = new CancellationTokenSource();
        CancellationToken token = _operationCancellation.Token;
        CancelButton.Visibility = Visibility.Visible;
        bool applied = false;
        SetBusy(true, "Preparing replacement texture...");
        try
        {
            await using var replacement = new MemoryStream();
            await CreateReplacementXbtAsync(dialog.FileName, entry, replacement, token);
            replacement.Position = 0;

            StatusText.Text = "Planning texture transaction...";
            XbtArchiveReplacementPlan plan = await XbtArchiveReplacementService.PlanAsync(
                source, entry.Index, entry.Entry.NameHash, replacement, token);
            replacement.Position = 0;

            StatusText.Text = "Building and validating dry-run archive...";
            XbtArchiveReplacementDryRunResult dryRun = await XbtArchiveReplacementService.DryRunAsync(
                source, entry.Index, entry.Entry.NameHash, replacement, temporaryRoot, token);
            if (!dryRun.Verified || !string.Equals(plan.PlanSha256, dryRun.Plan.PlanSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Texture dry-run did not reproduce the planned transaction.");
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"Replace:\n{entry.Name}\n\nArchive:\n{source.FatPath}\n\n" +
                $"Verified plan: {plan.PlanSha256[..16]}...\n" +
                $"Replacement size: {FormatBytes(plan.ReplacementLength)}\n\n" +
                "The game must be closed. Immutable .original backups will be created before the first write.",
                "Apply verified texture transaction",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                StatusText.Text = "Verified texture transaction not applied";
                return;
            }

            replacement.Position = 0;
            StatusText.Text = "Applying verified texture transaction...";
            FatV10ArchivePatchApplyResult result = await XbtArchiveReplacementService.ApplyAsync(
                source, plan.PlanSha256, entry.Index, entry.Entry.NameHash,
                replacement, temporaryRoot, token);
            applied = true;
            StatusText.Text = result.Backup.CreatedAny
                ? "Texture replaced and immutable backups created"
                : "Texture replaced and existing immutable backups retained";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            CancelButton.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }

        if (applied)
        {
            await LoadArchiveAsync(source.FatPath);
        }
    }

    private async Task CreateReplacementXbtAsync(
        string replacementPath,
        ArchiveEntryRow entry,
        Stream output,
        CancellationToken cancellationToken)
    {
        await using FileStream replacement = File.OpenRead(replacementPath);
        string extension = Path.GetExtension(replacementPath);
        if (string.Equals(extension, ".xbt", StringComparison.OrdinalIgnoreCase))
        {
            await replacement.CopyToAsync(output, cancellationToken);
            return;
        }

        using var template = new MemoryStream(entry.Entry.UncompressedSize);
        await using (FileStream data = File.OpenRead(_datPath!))
        {
            await FatV10PayloadExtractor.ExtractAsync(data, entry.Entry, template, cancellationToken);
        }

        template.Position = 0;
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            await XbtPngImporter.ImportAsync(template, replacement, output, cancellationToken);
        }
        else
        {
            await XbtDdsImporter.ImportAsync(template, replacement, output, cancellationToken);
        }
    }

    private void EditFcbClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy ||
            _fatPath is null ||
            _datPath is null ||
            EntriesGrid.SelectedItems.OfType<ArchiveEntryRow>()
                .Where(row => row.CanEditFcb)
                .OrderBy(row => row.Index)
                .Select(row => new FcbTransactionTarget(row.Index, row.Entry.NameHash, row.Name))
                .ToArray() is not { Length: > 0 } targets)
        {
            return;
        }

        var editor = new FcbTransactionWindow(
            new ArchivePair(_fatPath, _datPath),
            targets)
        {
            Owner = this,
        };
        editor.ShowDialog();
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenArchiveClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            LoadNamesClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void WindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isBusy && TryGetDroppedFatPath(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void WindowDrop(object sender, DragEventArgs e)
    {
        if (!_isBusy &&
            TryGetDroppedFatPath(e.Data, out string? fatPath) &&
            fatPath is not null &&
            await EnsureGameExecutableAsync())
        {
            await LoadArchiveAsync(fatPath);
        }
    }

    private static bool TryGetDroppedFatPath(IDataObject data, out string? fatPath)
    {
        fatPath = data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
                  string.Equals(Path.GetExtension(files[0]), ".fat", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;
        return fatPath is not null;
    }

    private static string[] FindAutomaticNameCatalogs(string fatPath)
    {
        string directory = Path.GetDirectoryName(fatPath) ?? string.Empty;
        string archiveName = Path.GetFileNameWithoutExtension(fatPath);
        string[] candidates =
        [
            Path.Combine(directory, $"{archiveName}.filelist"),
            Path.Combine(directory, "FCBConverterFileNames.list"),
            Path.Combine(AppContext.BaseDirectory, $"{archiveName}.filelist"),
            Path.Combine(AppContext.BaseDirectory, "archive-names.fc5.txt"),
            Path.Combine(AppContext.BaseDirectory, "data", "archive-names.fc5.txt"),
            GetNameCachePath(fatPath),
        ];

        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetDefaultArchiveDirectory(string gameDirectory)
    {
        string[] candidates =
        [
            Path.Combine(gameDirectory, "data_final", "pc"),
            Path.Combine(gameDirectory, "data_win32"),
            gameDirectory,
        ];
        return candidates.First(Directory.Exists);
    }

    private static string GetNameCachePath(string fatPath)
    {
        string normalizedPath = Path.GetFullPath(fatPath).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..16];
        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DuniaToolkit",
            "name-cache");
        return Path.Combine(cacheRoot, $"{Path.GetFileNameWithoutExtension(fatPath)}-{key}.filelist");
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy;
        LoadingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.IsEnabled = !busy;
        LoadNamesButton.IsEnabled = !busy;
        SelectGameExecutableMenuItem.IsEnabled = !busy;
        DiscoverNamesButton.IsEnabled = !busy && _index is not null;
        DiscoverNamesMenuItem.IsEnabled = !busy && _index is not null;
        EntriesGrid.IsEnabled = !busy;
        InspectMeshButton.IsEnabled = !busy;
        PreviewTextureButton.IsEnabled = !busy;
        ExportTexturePngButton.IsEnabled = !busy;
        CreateReplacementXbtButton.IsEnabled = !busy;
        ReplaceTextureButton.IsEnabled = !busy;
        EditFcbButton.IsEnabled = !busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        UpdatePageControls();

        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void UpdatePageControls()
    {
        PreviousPageButton.IsEnabled = !_isBusy && _index is not null && _pageIndex > 0;
        NextPageButton.IsEnabled = !_isBusy && _index is not null && _pageIndex + 1 < _pageCount;
    }

    private void UpdateNameCoverage(int resolved, int total)
    {
        NameCoverageText.Text = _resolver is null
            ? "not loaded"
            : $"{resolved:N0} / {total:N0}";
        NameCoverageText.ToolTip = _nameCatalogPath;
    }

    private void ShowError(string message)
    {
        StatusText.Text = "Operation failed";
        MessageBox.Show(this, message, "Dunia Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ExitClick(object sender, RoutedEventArgs e) => Close();

    private void CancelClick(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _filterTimer.Stop();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        Loaded -= WindowLoaded;
        PreviewKeyDown -= WindowPreviewKeyDown;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static int CountResolved(FatV10Index index, DuniaNameResolver? resolver) =>
        resolver is null ? 0 : index.Entries.Count(entry => resolver.Resolve(entry.NameHash).Count > 0);

    private static string? RenderName(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => names[0],
        _ => string.Join(" | ", names),
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private sealed record ArchiveLoadResult(FatV10Index Index, ArchiveEntryPage Page, int ResolvedCount);

    private sealed record ArchiveEntryPage(ArchiveEntryRow[] Rows, int MatchCount, int PageIndex);

    private sealed record ArchiveEntryRow(int Index, FatV10Entry Entry, string? ResolvedName)
    {
        private string PrimaryName => ResolvedName?.Split(" | ", StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty;

        public string Type { get; } = ResourceTypeIcon.GetKind(ResolvedName);

        public Geometry Icon => ResourceTypeIcon.For(Type);

        public bool IsResolved => ResolvedName is not null;

        public bool CanInspectMesh => PrimaryName.EndsWith(".xbg", StringComparison.OrdinalIgnoreCase);

        public bool CanPreviewTexture => PrimaryName.EndsWith(".xbt", StringComparison.OrdinalIgnoreCase);

        public bool CanEditFcb => PrimaryName.EndsWith(".fcb", StringComparison.OrdinalIgnoreCase);

        public string Name => ResolvedName ?? $"Entry {Index:D6}";

        public string IndexText => Index.ToString(CultureInfo.InvariantCulture);

        public string Hash => $"{Entry.NameHash:X16}";

        public string RawSize => FormatBytes(Entry.UncompressedSize);

        public string StoredSize => FormatBytes(Entry.StoredSize);

        public string Compression => Entry.CompressionScheme.ToString();

        public string Flags => Entry.IsEncrypted ? "Encrypted" : "None";

        public long Offset => Entry.Offset;

        public bool IsEncrypted => Entry.IsEncrypted;
    }
}
