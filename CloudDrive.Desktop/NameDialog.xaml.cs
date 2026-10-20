using System.Windows;
using System.Windows.Controls;
using CloudDrive.Core.Files;

namespace CloudDrive.Desktop;

public partial class NameDialog : Window
{
    public string FolderName => NameBox.Text.Trim();

    public NameDialog(string? location = null)
    {
        InitializeComponent();
        LocationText.Text = location is null ? "" : "Create in: " + location;
        LocationText.Visibility = location is null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ErrorText.Visibility = Visibility.Collapsed;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!FileNameValidator.IsValid(FolderName))
        {
            ErrorText.Text = "This name is not allowed. Avoid  < > : \" / \\ | ? *  and Windows reserved names such as CON or NUL.";
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
