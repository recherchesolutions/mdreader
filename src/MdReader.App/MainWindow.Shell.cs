using System.Windows;

namespace MdReader.App;

// File-association integration (first-run bar actions): implemented in Phase 4.
public partial class MainWindow
{
    private void HandleDefaultAppSet()
    {
        DefaultAppBar.Visibility = Visibility.Collapsed;
    }

    private void HandleDefaultAppDontAsk()
    {
        _settings.DontAskDefaultApp = true;
        _settings.Save();
        DefaultAppBar.Visibility = Visibility.Collapsed;
    }
}
