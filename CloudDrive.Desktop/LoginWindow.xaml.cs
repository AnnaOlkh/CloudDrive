using System.Net.Http;
using System.Windows;
using CloudDrive.Client;

namespace CloudDrive.Desktop;

public partial class LoginWindow : Window
{
    public CloudDriveApiClient? Api { get; private set; }
    public string UserName { get; private set; } = "";
    private HttpClient? _http;

    public LoginWindow()
    {
        InitializeComponent();
        var settings = UserSettings.Current;
        ServerBox.Text = settings.ServerUrl;
        UsernameBox.Text = settings.UserName;
        Loaded += (_, _) => (UsernameBox.Text.Length == 0 ? UsernameBox : (UIElement)PasswordBox).Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) => await AuthenticateAsync(false);
    private async void Register_Click(object sender, RoutedEventArgs e) => await AuthenticateAsync(true);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private async Task AuthenticateAsync(bool register)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        var userName = UsernameBox.Text.Trim();

        if (!Uri.TryCreate(ServerBox.Text.Trim(), UriKind.Absolute, out var server) ||
            (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
        {
            ShowError("Enter a valid server address starting with http:// or https://");
            ServerBox.Focus();
            return;
        }
        if (userName.Length == 0 || PasswordBox.Password.Length == 0)
        {
            ShowError("Username and password are required.");
            (userName.Length == 0 ? UsernameBox : (UIElement)PasswordBox).Focus();
            return;
        }
        if (register && (userName.Length is < 3 or > 32))
        {
            ShowError("Username must be 3–32 characters long.");
            UsernameBox.Focus();
            return;
        }
        if (register && PasswordBox.Password.Length < 6)
        {
            ShowError("Password must be at least 6 characters long.");
            PasswordBox.Focus();
            return;
        }

        SetBusy(true);
        var http = new HttpClient { BaseAddress = new Uri(server.ToString().TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
        try
        {
            var api = new CloudDriveApiClient(http);
            var response = register
                ? await api.RegisterAsync(userName, PasswordBox.Password)
                : await api.LoginAsync(userName, PasswordBox.Password);

            _http?.Dispose();
            _http = http;
            Api = api;
            UserName = response.UserName;

            var settings = UserSettings.Current;
            settings.ServerUrl = server.ToString();
            settings.UserName = response.UserName;
            settings.Save();

            DialogResult = true;
        }
        catch (Exception ex)
        {
            http.Dispose();
            ShowError(ex switch
            {
                ApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "Incorrect username or password.",
                ApiException { StatusCode: System.Net.HttpStatusCode.Conflict } when register => "That username is already taken.",
                HttpRequestException => "Could not reach the server. Check the address and your connection.",
                TaskCanceledException => "The server did not respond. Try again later.",
                _ => ex.Message
            });
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = RegisterButton.IsEnabled = !busy;
        ServerBox.IsEnabled = UsernameBox.IsEnabled = PasswordBox.IsEnabled = !busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
    }
}
