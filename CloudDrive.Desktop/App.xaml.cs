using System.Windows;
using System.Windows.Media;
using ModernWpf;

namespace CloudDrive.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Фіксована світла тема й акцентний колір бренду (не залежать від теми Windows).
        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
        ThemeManager.Current.AccentColor = Color.FromRgb(0x28, 0x64, 0xDC);
        base.OnStartup(e);
    }
}
