using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Dunia.Formats.Hashing;

namespace Dunia.Toolkit;

public partial class ResourceReferencesWindow : Window
{
    public ResourceReferencesWindow(
        string resourceName,
        IReadOnlyList<DuniaResourceReferenceMatch> matches,
        DuniaNameResolver? resolver)
    {
        InitializeComponent();
        Title = $"References — {resourceName}";
        SummaryText.Text = $"{matches.Count:N0} matches in {resourceName}";
        ReferencesGrid.ItemsSource = matches.Select(match => new ReferenceRow(
            $"0x{match.Offset:X}",
            match.Endianness == DuniaResourceReferenceEndianness.LittleEndian ? "LE" : "BE",
            match.ResourceHash.ToString("X16", CultureInfo.InvariantCulture),
            resolver is null ? string.Empty : string.Join(" | ", resolver.Resolve(match.ResourceHash))));
        Loaded += (_, _) => ReferencesGrid.Focus();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close();
                args.Handled = true;
            }
        };
    }

    private sealed record ReferenceRow(string Offset, string Endianness, string Hash, string Name)
    {
        public string AccessibleName => $"Offset {Offset}, {Endianness}, hash {Hash}, {Name}";
    }
}
