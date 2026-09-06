using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Dunia.Formats.Archives;
using Dunia.Formats.Fcb;
using Microsoft.Win32;

namespace Dunia.Toolkit;

public partial class FcbTransactionWindow : Window, IDisposable
{
    private static readonly string TemporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit",
        "fcb-transactions");

    private readonly ArchivePair _source;
    private readonly FcbTransactionTarget[] _targets;
    private readonly ObservableCollection<FcbMutationRow> _rows = [];
    private FcbValueSchema? _schema;
    private FcbArchiveTransactionPlanResult? _plan;
    private CancellationTokenSource? _operationCancellation;
    private string? _schemaPath;
    private bool _isBusy;
    private bool _isDisposed;

    internal FcbTransactionWindow(
        ArchivePair source,
        IReadOnlyList<FcbTransactionTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one FCB transaction target is required.", nameof(targets));
        }

        InitializeComponent();
        _source = source;
        _targets = targets.ToArray();
        FieldsGrid.ItemsSource = _rows;
        ResourceTitle.Text = _targets.Length == 1
            ? _targets[0].ResourceName
            : $"{_targets.Length:N0} FCB entries";
        ResourceIdentityText.Text = _targets.Length == 1
            ? FormattableString.Invariant($"Entry {_targets[0].EntryIndex} · {_targets[0].ResourceNameHash:X16}")
            : string.Join(", ", _targets.Select(target => target.EntryIndex.ToString(CultureInfo.InvariantCulture)));
        Loaded += WindowLoaded;
        Closing += WindowClosing;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        string defaultSchemaPath = Path.Combine(AppContext.BaseDirectory, "data", "fcb-schema.fc5.txt");
        if (File.Exists(defaultSchemaPath))
        {
            await LoadSchemaAsync(defaultSchemaPath);
            return;
        }

        StatusText.Text = "Load a complete FCB value schema to inspect editable fields";
    }

    private async void LoadSchemaClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Load FCB value schema",
            Filter = "FCB schema (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = _schemaPath is null ? null : Path.GetDirectoryName(_schemaPath),
        };

        if (dialog.ShowDialog(this) == true)
        {
            await LoadSchemaAsync(dialog.FileName);
        }
    }

    private async Task LoadSchemaAsync(string path)
    {
        await ExecuteAsync("Loading schema and FCB manifest...", async cancellationToken =>
        {
            string fullPath = Path.GetFullPath(path);
            FcbValueSchema schema = await Task.Run(() =>
            {
                using var input = new StreamReader(fullPath, detectEncodingFromByteOrderMarks: true);
                return FcbValueSchema.Load(input);
            }, cancellationToken);

            var manifests = new List<(FcbTransactionTarget Target, FcbArchiveMutationManifestResult Manifest)>(_targets.Length);
            foreach (FcbTransactionTarget target in _targets)
            {
                FcbArchiveMutationManifestResult manifest = await FcbArchiveMutationManifestService.CreateAsync(
                    _source,
                    target.EntryIndex,
                    target.ResourceNameHash,
                    schema,
                    cancellationToken);
                manifests.Add((target, manifest));
            }

            FcbNameResolver? names = await LoadDefaultNamesAsync(cancellationToken);

            ReplaceRows(manifests, names);
            _schema = schema;
            _schemaPath = fullPath;
            SchemaPathText.Text = fullPath;
            SchemaPathText.ToolTip = fullPath;
            int editableCount = manifests.Sum(item => item.Manifest.Entries.Count);
            int referencedCount = manifests.Sum(item => item.Manifest.ReferencedFieldCount);
            StatusText.Text = FormattableString.Invariant(
                $"Loaded {editableCount} editable fields across {manifests.Count} entries; {referencedCount} referenced fields excluded");
        });
    }

    private static async Task<FcbNameResolver?> LoadDefaultNamesAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "fcb-names.fc5.txt");
        if (!File.Exists(path))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            using var input = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            return FcbNameResolver.Load(input);
        }, cancellationToken);
    }

    private void ReplaceRows(
        IReadOnlyList<(FcbTransactionTarget Target, FcbArchiveMutationManifestResult Manifest)> manifests,
        FcbNameResolver? names)
    {
        foreach (FcbMutationRow row in _rows)
        {
            row.PropertyChanged -= RowPropertyChanged;
        }

        _rows.Clear();
        foreach ((FcbTransactionTarget target, FcbArchiveMutationManifestResult manifest) in manifests)
        {
            foreach (FcbArchiveMutationManifestEntry entry in manifest.Entries)
            {
                var row = new FcbMutationRow(
                    target.EntryIndex,
                    target.ResourceNameHash,
                    target.ResourceName,
                    entry.NodeIndex,
                    entry.FieldIndex,
                    entry.TypeHash,
                    entry.FieldHash,
                    entry.Codec,
                    entry.Value,
                    ResolveName(names, entry.TypeHash),
                    ResolveName(names, entry.FieldHash));
                row.PropertyChanged += RowPropertyChanged;
                _rows.Add(row);
            }
        }

        InvalidatePlan();
        UpdateControls();
    }

    private static string ResolveName(FcbNameResolver? resolver, uint hash)
    {
        IReadOnlyList<string> names = resolver?.Resolve(hash) ?? [];
        return names.Count == 0 ? "<unknown>" : string.Join(" | ", names);
    }

    private void RowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FcbMutationRow.Value) or nameof(FcbMutationRow.IsModified))
        {
            InvalidatePlan();
            UpdateControls();
        }
    }

    private async void PlanClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateRequest(out FcbArchiveTransactionEntry[] entries))
        {
            return;
        }

        await ExecuteAsync("Planning transaction...", async cancellationToken =>
        {
            FcbArchiveTransactionPlanResult plan = await FcbArchiveTransactionService.PlanAsync(
                _source,
                _schema!,
                entries,
                cancellationToken);
            SetPlan(plan);
            StatusText.Text = plan.NoOp
                ? "Plan verified as a semantic no-op"
                : $"Plan verified for {plan.Entries.Sum(entry => entry.Mutations.Count):N0} field changes";
        });
    }

    private async void DryRunClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateRequest(out FcbArchiveTransactionEntry[] entries) || _plan is null)
        {
            return;
        }

        string expectedPlanHash = _plan.PlanSha256;
        await ExecuteAsync("Building and validating temporary archive pair...", async cancellationToken =>
        {
            FcbArchiveTransactionDryRunResult result = await FcbArchiveTransactionService.DryRunAsync(
                _source,
                _schema!,
                entries,
                TemporaryRoot,
                cancellationToken);
            RequireMatchingPlan(expectedPlanHash, result.Plan.PlanSha256);
            if (!result.IsVerified)
            {
                throw new InvalidDataException("FCB transaction dry-run verification failed.");
            }

            StatusText.Text = $"Dry-run verified {result.ReplacementEntryCount:N0} replacement entries";
        });
    }

    private async void CreateCopyClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateRequest(out FcbArchiveTransactionEntry[] entries) || _plan is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Create modified archive copy",
            Filter = "Dunia archive index (*.fat)|*.fat",
            AddExtension = true,
            DefaultExt = ".fat",
            FileName = $"{Path.GetFileNameWithoutExtension(_source.FatPath)}.modified.fat",
            InitialDirectory = Path.GetDirectoryName(_source.FatPath),
            OverwritePrompt = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var destination = ArchivePair.FromIndex(dialog.FileName);
        if (File.Exists(destination.FatPath) || File.Exists(destination.DatPath))
        {
            ShowError("The destination FAT/DAT pair already exists. Choose a new name.");
            return;
        }

        string expectedPlanHash = _plan.PlanSha256;
        await ExecuteAsync("Creating and verifying modified archive copy...", async cancellationToken =>
        {
            FcbArchiveTransactionCopyResult result = await FcbArchiveTransactionService.CreateCopyAsync(
                _source,
                destination,
                _schema!,
                entries,
                TemporaryRoot,
                cancellationToken);
            RequireMatchingPlan(expectedPlanHash, result.Plan.PlanSha256);
            if (!result.PayloadsExact)
            {
                throw new InvalidDataException("Published FCB payload verification failed.");
            }

            StatusText.Text = $"Verified copy created: {result.OutputPair.FatPath}";
        });
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateRequest(out FcbArchiveTransactionEntry[] entries) || _plan is null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Apply {_rows.Count(row => row.IsModified):N0} pending field changes to:\n\n{_source.FatPath}\n\n" +
            "The game must be closed. Immutable .original backups are created before the first write. " +
            "The exact transaction plan will be revalidated before publication.",
            "Apply verified FCB transaction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        string expectedPlanHash = _plan.PlanSha256;
        await ExecuteAsync("Applying verified transaction...", async cancellationToken =>
        {
            FcbArchiveTransactionApplyResult result = await FcbArchiveTransactionService.ApplyAsync(
                _source,
                _schema!,
                entries,
                expectedPlanHash,
                TemporaryRoot,
                cancellationToken);
            if (!result.SemanticVerified)
            {
                throw new InvalidDataException("Published FCB semantic verification failed.");
            }

            await ReloadManifestAsync(cancellationToken);
            StatusText.Text = result.NoOp
                ? "No-op verified; archive was not written"
                : result.Backup?.CreatedAny == true
                    ? "Transaction applied and immutable backups created"
                    : "Transaction applied and existing immutable backups retained";
        });
    }

    private void DiscardClick(object sender, RoutedEventArgs e)
    {
        foreach (FcbMutationRow row in _rows)
        {
            row.Reset();
        }

        InvalidatePlan();
        UpdateControls();
        StatusText.Text = "Pending changes discarded";
    }

    private async Task ReloadManifestAsync(CancellationToken cancellationToken)
    {
        var manifests = new List<(FcbTransactionTarget Target, FcbArchiveMutationManifestResult Manifest)>(_targets.Length);
        foreach (FcbTransactionTarget target in _targets)
        {
            FcbArchiveMutationManifestResult manifest = await FcbArchiveMutationManifestService.CreateAsync(
                _source,
                target.EntryIndex,
                target.ResourceNameHash,
                _schema!,
                cancellationToken);
            manifests.Add((target, manifest));
        }

        FcbNameResolver? names = await LoadDefaultNamesAsync(cancellationToken);
        ReplaceRows(manifests, names);
    }

    private bool TryCreateRequest(out FcbArchiveTransactionEntry[] entries)
    {
        FieldsGrid.CommitEdit();
        entries = _rows
            .Where(row => row.IsModified)
            .GroupBy(row => new { row.EntryIndex, row.ResourceNameHash })
            .OrderBy(group => group.Key.EntryIndex)
            .Select(group => new FcbArchiveTransactionEntry(
                group.Key.EntryIndex,
                group.Key.ResourceNameHash,
                group.Select(row => new FcbArchiveFieldMutation(
                    row.NodeIndex,
                    row.FieldIndex,
                    row.TypeHashValue,
                    row.FieldHashValue,
                    row.Value)).ToArray()))
            .ToArray();
        return _schema is not null && entries.Length > 0;
    }

    private void SetPlan(FcbArchiveTransactionPlanResult plan)
    {
        _plan = plan;
        PlanStateText.Text = plan.NoOp ? "verified no-op" : "verified";
        PlanHashText.Text = $"Plan SHA-256: {plan.PlanSha256}";
        PlanHashText.ToolTip = plan.PlanSha256;
        UpdateControls();
    }

    private void InvalidatePlan()
    {
        _plan = null;
        PlanStateText.Text = "not created";
        PlanHashText.Text = "Plan SHA-256: -";
        PlanHashText.ToolTip = null;
    }

    private void UpdateControls()
    {
        int pendingCount = _rows.Count(row => row.IsModified);
        bool hasPending = pendingCount > 0;
        PendingCountText.Text = pendingCount.ToString("N0", CultureInfo.CurrentCulture);
        FieldsGrid.IsReadOnly = _isBusy;
        LoadSchemaButton.IsEnabled = !_isBusy;
        DiscardButton.IsEnabled = !_isBusy && hasPending;
        PlanButton.IsEnabled = !_isBusy && hasPending && _schema is not null;
        DryRunButton.IsEnabled = !_isBusy && hasPending && _plan is not null;
        CreateCopyButton.IsEnabled = !_isBusy && hasPending && _plan is not null;
        ApplyButton.IsEnabled = !_isBusy && hasPending && _plan is not null;
        CancelButton.Visibility = _isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ExecuteAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        StatusText.Text = status;
        UpdateControls();
        try
        {
            await operation(_operationCancellation.Token);
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
            _isBusy = false;
            UpdateControls();
        }
    }

    private static void RequireMatchingPlan(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Archive changed after planning. Expected plan {expected}, got {actual}.");
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = "Operation failed";
        MessageBox.Show(this, message, "Dunia Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CancelClick(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isBusy)
        {
            e.Cancel = true;
            _operationCancellation?.Cancel();
            StatusText.Text = "Cancelling current operation...";
        }
    }

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

        foreach (FcbMutationRow row in _rows)
        {
            row.PropertyChanged -= RowPropertyChanged;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        Closing -= WindowClosing;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class FcbMutationRow : INotifyPropertyChanged
    {
        private string _value;

        public FcbMutationRow(
            int entryIndex,
            ulong resourceNameHash,
            string resourceName,
            int nodeIndex,
            int fieldIndex,
            uint typeHash,
            uint fieldHash,
            FcbValueKind codec,
            string originalValue,
            string typeName,
            string fieldName)
        {
            EntryIndex = entryIndex;
            ResourceNameHash = resourceNameHash;
            ResourceName = resourceName;
            NodeIndex = nodeIndex;
            FieldIndex = fieldIndex;
            TypeHashValue = typeHash;
            FieldHashValue = fieldHash;
            Codec = codec;
            OriginalValue = originalValue;
            _value = originalValue;
            TypeName = typeName;
            FieldName = fieldName;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int EntryIndex { get; }

        public ulong ResourceNameHash { get; }

        public string ResourceName { get; }

        public int NodeIndex { get; }

        public int FieldIndex { get; }

        public uint TypeHashValue { get; }

        public uint FieldHashValue { get; }

        public string TypeHash => TypeHashValue.ToString("X8", CultureInfo.InvariantCulture);

        public string FieldHash => FieldHashValue.ToString("X8", CultureInfo.InvariantCulture);

        public FcbValueKind Codec { get; }

        public string OriginalValue { get; }

        public string TypeName { get; }

        public string FieldName { get; }

        public string State => IsModified ? "●" : string.Empty;

        public bool IsModified => !string.Equals(OriginalValue, Value, StringComparison.Ordinal);

        public string Value
        {
            get => _value;
            set
            {
                value ??= string.Empty;
                if (string.Equals(_value, value, StringComparison.Ordinal))
                {
                    return;
                }

                _value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(State));
            }
        }

        public void Reset() => Value = OriginalValue;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed record FcbTransactionTarget(int EntryIndex, ulong ResourceNameHash, string ResourceName);
