using Cricket_Models.ConfigurationModels;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cricket_Services.PushServices;

public record PushResult(string Token, bool Success, string? MessageId, string? Error, bool InvalidToken);

public interface IPushSender
{
    /// <summary>False when no Firebase credentials are configured; sends then fail with "push_disabled" instead of throwing.</summary>
    bool IsEnabled { get; }
    Task<List<PushResult>> SendAsync(IReadOnlyList<string> tokens, string title, string body, Dictionary<string, string> data);
    Task<string?> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string> data);
    Task DeleteFirebaseUserAsync(string uid);
}

/// <summary>Firebase Admin SDK wrapper (FCM + user management). Singleton: initialises FirebaseApp once.</summary>
public class PushSender : IPushSender
{
    private const string ChannelId = "cricket_updates";
    private readonly ILogger<PushSender> _logger;

    public bool IsEnabled { get; }

    public PushSender(IOptions<FirebaseConfig> options, ILogger<PushSender> logger)
    {
        _logger = logger;
        var cfg = options.Value;
        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                GoogleCredential credential;
                if (!string.IsNullOrWhiteSpace(cfg.CredentialsPath) && File.Exists(Path.GetFullPath(cfg.CredentialsPath)))
                {
                    credential = GoogleCredential.FromFile(Path.GetFullPath(cfg.CredentialsPath));
                }
                else
                {
                    credential = GoogleCredential.GetApplicationDefault();   // GOOGLE_APPLICATION_CREDENTIALS / workload identity
                }
                FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = cfg.ProjectId });
            }
            IsEnabled = true;
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            _logger.LogWarning(ex, "Firebase Admin not initialised; push notifications and Firebase user deletion are disabled. " +
                                   "Set FirebaseConfig:CredentialsPath or GOOGLE_APPLICATION_CREDENTIALS.");
        }
    }

    public async Task<List<PushResult>> SendAsync(IReadOnlyList<string> tokens, string title, string body, Dictionary<string, string> data)
    {
        var results = new List<PushResult>();
        if (tokens.Count == 0) return results;
        if (!IsEnabled)
        {
            return tokens.Select(t => new PushResult(t, false, null, "push_disabled", false)).ToList();
        }

        foreach (var chunk in tokens.Chunk(500))
        {
            var messages = chunk.Select(t => BuildMessage(t, null, title, body, data)).ToList();
            try
            {
                var batch = await FirebaseMessaging.DefaultInstance.SendEachAsync(messages);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var r = batch.Responses[i];
                    if (r.IsSuccess)
                    {
                        results.Add(new PushResult(chunk[i], true, r.MessageId, null, false));
                    }
                    else
                    {
                        var ex = r.Exception;
                        var invalid = ex?.MessagingErrorCode == MessagingErrorCode.Unregistered
                                      || ex?.MessagingErrorCode == MessagingErrorCode.InvalidArgument;
                        results.Add(new PushResult(chunk[i], false, null, ex?.MessagingErrorCode?.ToString() ?? ex?.Message, invalid));
                    }
                }
            }
            catch (Exception ex)
            {
                // Never log tokens.
                _logger.LogError(ex, "FCM batch send failed for {Count} token(s)", chunk.Length);
                results.AddRange(chunk.Select(t => new PushResult(t, false, null, "send_failed", false)));
            }
        }
        return results;
    }

    public async Task<string?> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string> data)
    {
        if (!IsEnabled) throw new InvalidOperationException("Firebase is not configured on this server.");
        return await FirebaseMessaging.DefaultInstance.SendAsync(BuildMessage(null, topic, title, body, data));
    }

    public async Task DeleteFirebaseUserAsync(string uid)
    {
        if (!IsEnabled) return;
        try
        {
            await FirebaseAuth.DefaultInstance.DeleteUserAsync(uid);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            // already gone
        }
    }

    private static Message BuildMessage(string? token, string? topic, string title, string body, Dictionary<string, string> data) => new()
    {
        Token = token,
        Topic = topic,
        Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
        Data = data,
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            Notification = new AndroidNotification { ChannelId = ChannelId }
        }
    };
}
