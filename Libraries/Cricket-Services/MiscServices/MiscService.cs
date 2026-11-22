using Cricket_Models.ConfigurationModels;
using Cricket_Models.MiscModels;
using Cricket_Repository.Entity;
using Cricket_Repository.UserRepository;
using Cricket_Models.CommonModels;
using Microsoft.Extensions.Options;

namespace Cricket_Services.MiscServices;

public interface IMiscService
{
    ConfigResponse GetConfig();
    Task AddFeedbackAsync(string uid, FeedbackRequest request);
}

public class MiscService : IMiscService
{
    private readonly AppConfig _config;
    private readonly IUserRepo _users;

    public MiscService(IOptions<AppConfig> config, IUserRepo users)
    {
        _config = config.Value;
        _users = users;
    }

    public ConfigResponse GetConfig() => new()
    {
        MinAppVersion = _config.MinAppVersion,
        Features = _config.Features
    };

    public Task AddFeedbackAsync(string uid, FeedbackRequest request) =>
        _users.AddFeedbackAsync(new Feedback
        {
            UserId = uid,
            Message = request.Message.Trim(),
            AppVersion = request.AppVersion,
            CreatedAt = Clock.UtcNow()
        });
}
