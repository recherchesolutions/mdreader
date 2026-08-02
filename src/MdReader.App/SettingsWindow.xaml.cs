using System.IO;
using System.Windows;
using System.Windows.Controls;
using MdReader.Core;

namespace MdReader.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<CheckBox> _extensionBoxes = [];

    // "Full width" (the default) uses a huge max-width so the CSS calc() for
    // the TOC rail stays valid (an actual `none` would break it).
    private static readonly Dictionary<string, int?> ContentWidthChoices = new()
    {
        ["Full width (default)"] = 10000,
        ["Extra wide (1200 px)"] = 1200,
        ["Wide (960 px)"] = 960,
        ["Reading measure (720 px)"] = null,
    };

    /// <summary>Set when the user clicked "Set as default" — handled by the owner window.</summary>
    public bool SetDefaultAppRequested { get; private set; }

    public SettingsWindow(AppSettings settings, IReadOnlyDictionary<string, string> associationOwners)
    {
        _settings = settings;
        InitializeComponent();

        DefaultModeBox.ItemsSource = Enum.GetValues<DefaultMode>();
        DefaultModeBox.SelectedItem = settings.DefaultMode;

        ThemeBox.ItemsSource = Enum.GetValues<ThemeChoice>();
        ThemeBox.SelectedItem = settings.Theme;

        var customThemes = new List<string> { "(none)" };
        customThemes.AddRange(ThemeLoader.ListCustomThemes());
        CustomThemeBox.ItemsSource = customThemes;
        CustomThemeBox.SelectedItem = settings.CustomTheme is { } custom && customThemes.Contains(custom)
            ? custom
            : "(none)";

        ContentWidthBox.ItemsSource = ContentWidthChoices.Keys;
        ContentWidthBox.SelectedItem = ContentWidthChoices
            .FirstOrDefault(kv => kv.Value == settings.ContentWidthOverride, ContentWidthChoices.First()).Key;

        FontFamilyBox.Text = settings.FontFamilyOverride ?? string.Empty;
        FontSizeBox.Text = settings.FontSizeOverride?.ToString() ?? string.Empty;
        LineSpacingBox.ItemsSource = new[] { 1.4, 1.5, 1.65, 1.8, 2.0 };
        LineSpacingBox.SelectedItem = settings.LineSpacing;
        ParagraphSpacingBox.ItemsSource = new[] { 0.5, 0.75, 1.0, 1.25, 1.5 };
        ParagraphSpacingBox.SelectedItem = settings.ParagraphSpacingEm;

        LineEndingBox.ItemsSource = Enum.GetValues<LineEndingPolicy>();
        LineEndingBox.SelectedItem = settings.LineEndingPolicy;
        AssetDirectoryBox.Text = settings.AssetDirectoryName;
        ExportPresetBox.ItemsSource = Enum.GetValues<ExportPreset>();
        ExportPresetBox.SelectedItem = settings.ExportPreset;

        SplitDefaultBox.IsChecked = settings.SplitViewByDefault;
        RestoreSessionBox.IsChecked = settings.RestorePreviousSession;
        RemoteImagesBox.IsChecked = settings.LoadRemoteImages;
        UpdateCheckBox.IsChecked = settings.CheckForUpdates;

        if (Services.PackagedContext.IsPackaged)
        {
            // Store installs update through the Store, and file associations are
            // fixed by the package manifest — hide the knobs that don't apply.
            UpdateCheckBox.Visibility = System.Windows.Visibility.Collapsed;
            ExtensionList.Visibility = System.Windows.Visibility.Collapsed;
        }

        foreach (var extension in FileTypes.OptionalExtensions)
        {
            var box = new CheckBox
            {
                Content = extension,
                IsChecked = settings.ExtraRegisteredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 2, 0, 2),
            };
            _extensionBoxes.Add(box);
            ExtensionList.Items.Add(box);
        }

        foreach (var (extension, owner) in associationOwners.OrderBy(kv => kv.Key))
        {
            OwnersList.Items.Add(new TextBlock { Text = $"{extension,-10} {owner}" });
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.DefaultMode = (DefaultMode)DefaultModeBox.SelectedItem;
        _settings.Theme = (ThemeChoice)ThemeBox.SelectedItem;
        _settings.CustomTheme = CustomThemeBox.SelectedItem as string is "(none)" or null
            ? null
            : (string)CustomThemeBox.SelectedItem;
        _settings.ContentWidthOverride = ContentWidthChoices[(string)ContentWidthBox.SelectedItem];
        _settings.FontFamilyOverride = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? null : FontFamilyBox.Text.Trim();
        _settings.FontSizeOverride = int.TryParse(FontSizeBox.Text, out var size) && size is >= 8 and <= 40 ? size : null;
        _settings.LineSpacing = LineSpacingBox.SelectedItem is double lineSpacing ? lineSpacing : 1.65;
        _settings.ParagraphSpacingEm = ParagraphSpacingBox.SelectedItem is double paragraphSpacing ? paragraphSpacing : 1.0;
        _settings.LineEndingPolicy = (LineEndingPolicy)LineEndingBox.SelectedItem;
        var assetName = AssetDirectoryBox.Text.Trim();
        _settings.AssetDirectoryName = assetName.Length > 0 &&
            !Path.IsPathRooted(assetName) && !assetName.Split('/', '\\').Contains("..")
            ? assetName
            : "assets";
        _settings.ExportPreset = (ExportPreset)ExportPresetBox.SelectedItem;
        _settings.SplitViewByDefault = SplitDefaultBox.IsChecked == true;
        _settings.RestorePreviousSession = RestoreSessionBox.IsChecked == true;
        _settings.LoadRemoteImages = RemoteImagesBox.IsChecked == true;
        _settings.CheckForUpdates = UpdateCheckBox.IsChecked == true;

        _settings.ExtraRegisteredExtensions = _extensionBoxes
            .Where(b => b.IsChecked == true)
            .Select(b => (string)b.Content)
            .ToList();

        DialogResult = true;
    }

    private void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        SetDefaultAppRequested = true;
        Close();
    }
}
