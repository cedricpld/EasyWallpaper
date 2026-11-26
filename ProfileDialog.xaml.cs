using System.Windows;

namespace CGWallpaper;

public partial class ProfileDialog : Window
{
    public string ProfileName { get; private set; } = "";

    public ProfileDialog(string defaultName = "")
    {
        InitializeComponent();
        TxtName.Text = defaultName;
        TxtName.SelectAll();
        TxtName.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "Veuillez entrer un nom de profil.", "Nom requis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ProfileName = name;
        DialogResult = true;
    }
}
