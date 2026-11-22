using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text;

namespace Cricket_Repository.Entity;

/// <summary>Maps to the schema created by Database/CricketDB_Deploy.sql (database-first; no migrations).</summary>
public class CricketContext : DbContext
{
    public CricketContext(DbContextOptions<CricketContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<TeamPlayer> TeamPlayers => Set<TeamPlayer>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<Innings> Innings => Set<Innings>();
    public DbSet<Ball> Balls => Set<Ball>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<MatchFollower> MatchFollowers => Set<MatchFollower>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<MatchInvitation> MatchInvitations => Set<MatchInvitation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // All timestamps are UTC; make sure values read back are flagged Utc so JSON serialises with "Z".
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
        // EF Core 7 has no built-in DateOnly mapping; store it in a SQL "date" column.
        builder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>().HaveColumnType("date");
        builder.Properties<DateOnly?>().HaveConversion<NullableDateOnlyConverter>().HaveColumnType("date");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().ToTable("users").HasKey(x => x.Id);
        b.Entity<UserDevice>().ToTable("user_devices").HasKey(x => x.Id);
        b.Entity<Team>().ToTable("teams").HasKey(x => new { x.UserId, x.Id });
        b.Entity<Player>().ToTable("players").HasKey(x => new { x.UserId, x.Id });
        b.Entity<TeamPlayer>().ToTable("team_players").HasKey(x => new { x.UserId, x.TeamId, x.PlayerId });
        b.Entity<Match>().ToTable("matches").HasKey(x => new { x.UserId, x.Id });
        b.Entity<MatchPlayer>().ToTable("match_players").HasKey(x => new { x.UserId, x.MatchId, x.PlayerId });
        b.Entity<Innings>().ToTable("innings").HasKey(x => new { x.UserId, x.MatchId, x.InningsNo });
        b.Entity<Ball>().ToTable("balls").HasKey(x => new { x.UserId, x.MatchId, x.InningsNo, x.Seq });
        b.Entity<Notification>().ToTable("notifications").HasKey(x => x.Id);
        b.Entity<NotificationPreference>().ToTable("notification_preferences").HasKey(x => x.UserId);
        b.Entity<MatchFollower>().ToTable("match_followers").HasKey(x => new { x.MatchOwnerId, x.MatchId, x.FollowerId });
        b.Entity<Feedback>().ToTable("feedback").HasKey(x => x.Id);
        b.Entity<MatchInvitation>().ToTable("match_invitations").HasKey(x => x.Id);

        // Ids are supplied by the client (or generated in code) except the three identity columns.
        b.Entity<Notification>().Property(x => x.Id).ValueGeneratedOnAdd();
        b.Entity<UserDevice>().Property(x => x.Id).ValueGeneratedOnAdd();
        b.Entity<Feedback>().Property(x => x.Id).ValueGeneratedOnAdd();
        b.Entity<Innings>().Property(x => x.InningsNo).ValueGeneratedNever();
        b.Entity<Ball>().Property(x => x.InningsNo).ValueGeneratedNever();
        b.Entity<Ball>().Property(x => x.Seq).ValueGeneratedNever();

        b.Entity<Player>().Property(x => x.HeightCm).HasPrecision(5, 1);
        b.Entity<Player>().Property(x => x.WeightKg).HasPrecision(5, 1);

        // snake_case columns: TeamAId -> team_a_id, FcmToken -> fcm_token ...
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prevLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var acronymEnd = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (prevLowerOrDigit || acronymEnd) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static DateTime AsUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        _ => v.ToUniversalTime()
    };

    private class UtcConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcConverter() : base(v => AsUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }

    private class NullableUtcConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcConverter() : base(
            v => v.HasValue ? AsUtc(v.Value) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
    }

    private class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
    {
        public DateOnlyConverter() : base(v => v.ToDateTime(TimeOnly.MinValue), v => DateOnly.FromDateTime(v)) { }
    }

    private class NullableDateOnlyConverter : ValueConverter<DateOnly?, DateTime?>
    {
        public NullableDateOnlyConverter() : base(
            v => v.HasValue ? v.Value.ToDateTime(TimeOnly.MinValue) : null,
            v => v.HasValue ? DateOnly.FromDateTime(v.Value) : null) { }
    }
}
