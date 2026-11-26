using System.Windows;

namespace CGWallpaper;

public partial class UpdateDialog : Window
{
    public UpdateDialog(Version currentVersion, string latestTag)
    {
        InitializeComponent();
        TxtVersions.Text = $"Version actuelle : v{currentVersion}  ·  Nouvelle : {latestTag}";
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
