using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace CGWallpaper;

public partial class UrlDialog : Window
{
    public WallpaperType SelectedType { get; private set; }
    public string ResultUrl { get; private set; } = "";
    public string ResultSelector { get; private set; } = "";
    public int RefreshSeconds { get; private set; } = 60;
    public bool MuteAudio { get; private set; } = true;
    public bool Cleared { get; private set; }

    public UrlDialog(MonitorWallpaperConfig? existing)
    {
        InitializeComponent();
        if (existing != null && (existing.Type == WallpaperType.WebPage || existing.Type == WallpaperType.WebcamSnapshot || existing.Type == WallpaperType.WebImage))
        {
            TxtUrl.Text = existing.Source;
            TxtSelector.Text = existing.Selector;
            ChkMute.IsChecked = existing.MuteAudio;
            if (existing.Type == WallpaperType.WebcamSnapshot)
            {
                RbSnapshot.IsChecked = true;
            }
            else if (existing.Type == WallpaperType.WebImage)
            {
                RbWebImage.IsChecked = true;
            }
            if (existing.Type == WallpaperType.WebcamSnapshot || existing.Type == WallpaperType.WebImage)
            {
                foreach (ComboBoxItem item in CmbInterval.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int sec) && sec == existing.RefreshSeconds)
                    {
                        CmbInterval.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        else
        {
            BtnDisable.Visibility = Visibility.Collapsed;
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelRefresh == null || ChkMute == null || PanelSelector == null) return;
        bool isSnap = RbSnapshot.IsChecked == true;
        bool isWebImg = RbWebImage.IsChecked == true;
        PanelSelector.Visibility = isWebImg ? Visibility.Visible : Visibility.Collapsed;
        PanelRefresh.Visibility = (isSnap || isWebImg) ? Visibility.Visible : Visibility.Collapsed;
        ChkMute.Visibility = (!isSnap && !isWebImg) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnDisable_Click(object sender, RoutedEventArgs e)
    {
        Cleared = true;
        DialogResult = true;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(this, "Veuillez entrer une URL valide commençant par http:// ou https://", "URL invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RbSnapshot.IsChecked == true || RbWebImage.IsChecked == true)
        {
            SelectedType = RbWebImage.IsChecked == true ? WallpaperType.WebImage : WallpaperType.WebcamSnapshot;
            ResultUrl = url;
            ResultSelector = TxtSelector.Text.Trim();
            if (CmbInterval.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int sec))
                RefreshSeconds = sec;
        }
        else
        {
            SelectedType = WallpaperType.WebPage;
            ResultUrl = NormalizeWebUrl(url);
            MuteAudio = ChkMute.IsChecked == true;
        }
        DialogResult = true;
    }

    private static string NormalizeWebUrl(string url)
    {
        var m = Regex.Match(url, @"(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/live/)([A-Za-z0-9_-]{11})");
        if (m.Success)
        {
            string id = m.Groups[1].Value;
            return $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1&controls=0&loop=1&playlist={id}&rel=0";
        }
        return url;
    }
}
