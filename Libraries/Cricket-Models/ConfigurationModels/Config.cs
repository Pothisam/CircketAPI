namespace Cricket_Models.ConfigurationModels;

public class FirebaseConfig
{
    public string ProjectId { get; set; } = "cricket-run-notification";
    /// <summary>Path to the service-account JSON. Leave empty to rely on GOOGLE_APPLICATION_CREDENTIALS. Never commit this file.</summary>
    public string? CredentialsPath { get; set; }
}

public class AppConfig
{
    public string MinAppVersion { get; set; } = "1.0.0";
    public Dictionary<string, bool> Features { get; set; } = new() { ["sharing"] = true };
    /// <summary>Base for share links, e.g. https://cricket.example.com. Falls back to the request host.</summary>
    public string? PublicBaseUrl { get; set; }
}
