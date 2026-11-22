using Cricket_Repository.Infrastructure;
using Cricket_Services.AuthServices;
using Cricket_Services.DeviceServices;
using Cricket_Services.InvitationServices;
using Cricket_Services.MatchServices;
using Cricket_Services.MembershipServices;
using Cricket_Services.MiscServices;
using Cricket_Services.NotificationServices;
using Cricket_Services.PlayerServices;
using Cricket_Services.ProfileServices;
using Cricket_Services.PushServices;
using Cricket_Services.StatsServices;
using Cricket_Services.SyncServices;
using Cricket_Services.TeamServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cricket_Services.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection ServicesDependencyInjection(this IServiceCollection service, IConfiguration config)
    {
        service.RepositoryDependencyInjection(config);
        service.AddMemoryCache();

        service.AddScoped<IAuthService, AuthService>();
        service.AddScoped<IDeviceService, DeviceService>();
        service.AddScoped<ITeamService, TeamService>();
        service.AddScoped<IPlayerService, PlayerService>();
        service.AddScoped<IProfileService, ProfileService>();
        service.AddScoped<IMatchService, MatchService>();
        service.AddScoped<IMembershipService, MembershipService>();
        service.AddScoped<IInvitationService, InvitationService>();
        service.AddScoped<ISyncService, SyncService>();
        service.AddScoped<IStatsService, StatsService>();
        service.AddScoped<IMiscService, MiscService>();
        service.AddScoped<INotificationService, NotificationService>();
        service.AddScoped<IMatchEventNotifier, MatchEventNotifier>();

        // Firebase Admin (FCM + user management) and the background notification queue.
        service.AddSingleton<IPushSender, PushSender>();
        service.AddSingleton<NotificationDispatcher>();
        service.AddSingleton<INotificationDispatcher>(sp => sp.GetRequiredService<NotificationDispatcher>());
        service.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        return service;
    }
}
