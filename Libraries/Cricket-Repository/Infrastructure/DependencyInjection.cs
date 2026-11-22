using Cricket_Repository.Entity;
using Cricket_Repository.InvitationRepository;
using Cricket_Repository.MatchRepository;
using Cricket_Repository.MembershipRepository;
using Cricket_Repository.NotificationRepository;
using Cricket_Repository.PlayerRepository;
using Cricket_Repository.TeamRepository;
using Cricket_Repository.UserRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cricket_Repository.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection RepositoryDependencyInjection(this IServiceCollection service, IConfiguration config)
    {
        service.AddScoped<IUserRepo, UserRepo>();
        service.AddScoped<ITeamRepo, TeamRepo>();
        service.AddScoped<IPlayerRepo, PlayerRepo>();
        service.AddScoped<IMatchRepo, MatchRepo>();
        service.AddScoped<IMembershipRepo, MembershipRepo>();
        service.AddScoped<IInvitationRepo, InvitationRepo>();
        service.AddScoped<INotificationRepo, NotificationRepo>();
        service.AddDbContext<CricketContext>(option =>
            option.UseSqlServer(config.GetConnectionString("DefaultConnection")));
        return service;
    }
}
