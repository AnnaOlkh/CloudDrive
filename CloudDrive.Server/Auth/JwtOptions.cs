namespace CloudDrive.Server.Auth;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "CloudDrive";
    public string Audience { get; set; } = "CloudDrive";
    public int ExpiresMinutes { get; set; } = 480;
}
