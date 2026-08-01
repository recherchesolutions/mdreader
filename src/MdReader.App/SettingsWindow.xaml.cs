using System.Windows;
using System.Windows.Controls;
using MdReader.Core;

namespace MdReader.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<CheckBox> _extensionBoxes = [];

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

        FontFamilyBox.Text = settings.FontFamilyOverride ?? string.Empty;
        FontSizeBox.Text = settings.FontSizeOverride?.ToString() ?? string.Empty;

        LineEndingBox.ItemsSource = Enum.GetValues<LineEndingPolicy>();
        LineEndingBox.SelectedItem = settings.LineEndingPolicy;

        SplitDefaultBox.IsChecked = settings.SplitViewByDefault;
        RemoteImagesBox.IsChecked = settings.LoadRemoteImages;
        UpdateCheckBox.IsChecked = settings.CheckForUpdates;

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
        _settings.FontFamilyOverride = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? null : FontFamilyBox.Text.Trim();
        _settings.FontSizeOverride = int.TryParse(FontSizeBox.Text, out var size) && size is >= 8 and <= 40 ? size : null;
        _settings.LineEndingPolicy = (LineEndingPolicy)LineEndingBox.SelectedItem;
        _settings.SplitViewByDefault = SplitDefaultBox.IsChecked == true;
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
