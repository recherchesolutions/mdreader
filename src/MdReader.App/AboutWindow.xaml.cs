using System.Windows;

namespace MdReader.App;

public partial class AboutWindow : Window
{
    private const string CompanyUrl = "https://recherchesolutions.com";

    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "dev";
        VersionText.Text = $"version {version}";
    }

    private void OnCompanyLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CompanyUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Browser launch failed; the tooltip already shows the URL.
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
