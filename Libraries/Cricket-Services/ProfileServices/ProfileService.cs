using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.PlayerModels;
using Cricket_Repository.Entity;
using Cricket_Repository.PlayerRepository;
using Cricket_Repository.UserRepository;
using Cricket_Services.PlayerServices;

namespace Cricket_Services.ProfileServices;

public interface IProfileService
{
    /// <summary>
    /// Sign-in hook. Makes sure the user is a player in their own list and that every player (in any
    /// roster) that was added with this user's e-mail is linked to the user.
    /// </summary>
    Task EnsureSelfPlayerAsync(User user);
    Task<ProfileDto> GetAsync(string uid);
    Task<ProfileDto> UpdateAsync(string uid, UpdateProfileRequest request);
    /// <summary>Pushes the user's public profile to every copy of them in other players' lists.</summary>
    Task MirrorAsync(User user, Player self);
    /// <summary>Copies a changed display name / photo from the user to their own player.</summary>
    Task SyncSelfFromUserAsync(User user);
}

public class ProfileService : IProfileService
{
    private readonly IUserRepo _users;
    private readonly IPlayerRepo _players;

    public ProfileService(IUserRepo users, IPlayerRepo players)
    {
        _users = users;
        _players = players;
    }

    public async Task EnsureSelfPlayerAsync(User user)
    {
        var self = await GetOrCreateSelfAsync(user);
        var email = PlayerRules.NormalizeEmail(user.Email);

        // Other people's manual players that carry this e-mail become "this user" ...
        if (email != null) await _players.LinkByEmailAsync(email, user.Id);
        // ... and every copy of this user shows their real profile.
        await MirrorAsync(user, self);
    }

    private async Task<Player> GetOrCreateSelfAsync(User user)
    {
        var now = Clock.UtcNow();
        var email = PlayerRules.NormalizeEmail(user.Email);

        var self = await _players.GetSelfAsync(user.Id);
        if (self == null)
        {
            // The user may have already added themselves by hand with their own e-mail: adopt that one.
            var adopt = email == null ? null : await _players.FindByEmailAsync(user.Id, email);
            if (adopt != null)
            {
                adopt.IsSelf = true;
                adopt.LinkedUserId = user.Id;
                adopt.UpdatedAt = now;
                await _players.SaveAsync(adopt, false);
                return adopt;
            }

            var created = new Player
            {
                Id = PlayerRules.SelfPlayerId(user.Id),
                UserId = user.Id,
                Name = DisplayNameOf(user),
                Email = email,
                PhotoUrl = user.PhotoUrl,
                Gender = user.Gender,
                DateOfBirth = user.DateOfBirth,
                City = user.City,
                LinkedUserId = user.Id,
                IsSelf = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _players.SaveAsync(created, true);
            return created;
        }

        var changed = false;
        if (self.DeletedAt != null) { self.DeletedAt = null; changed = true; }
        if (self.LinkedUserId != user.Id) { self.LinkedUserId = user.Id; changed = true; }
        if (email != null && self.Email != email) { self.Email = email; changed = true; }
        // the sign-in photo (Google picture) may not have been known when the player was created
        if (string.IsNullOrEmpty(self.PhotoUrl) && !string.IsNullOrEmpty(user.PhotoUrl)) { self.PhotoUrl = user.PhotoUrl; changed = true; }
        if (changed)
        {
            self.UpdatedAt = now;
            await _players.SaveAsync(self, false);
        }
        return self;
    }

    public Task MirrorAsync(User user, Player self) =>
        _players.MirrorProfileAsync(user.Id, self.Name, self.Role, self.BattingStyle, self.BowlingStyle, user.City, user.PhotoUrl);

    public async Task<ProfileDto> GetAsync(string uid)
    {
        var user = await _users.GetUserAsync(uid) ?? throw ApiException.NotFound("User");
        var self = await _players.GetSelfAsync(uid);
        if (self == null)
        {
            await EnsureSelfPlayerAsync(user);
            self = await _players.GetSelfAsync(uid);
        }
        return ToDto(user, self!);
    }

    public async Task<ProfileDto> UpdateAsync(string uid, UpdateProfileRequest r)
    {
        var name = r.DisplayName?.Trim() ?? "";
        new ValidationBuilder()
            .Check(name.Length is > 0 and <= 120, "displayName", "required, max 120 characters")
            .OneOf(r.Role, CricketEnums.PlayerRoles, "role")
            .OneOf(r.BattingStyle, CricketEnums.BattingStyles, "battingStyle")
            .OneOf(r.BowlingStyle, CricketEnums.BowlingStyles, "bowlingStyle")
            .CheckProfile(r.Gender, r.DateOfBirth, r.HeightCm, r.WeightKg, r.JerseyNumber)
            .ThrowIfAny();

        var user = await _users.GetUserAsync(uid) ?? throw ApiException.NotFound("User");
        var self = await _players.GetSelfAsync(uid);
        if (self == null)
        {
            await EnsureSelfPlayerAsync(user);
            self = (await _players.GetSelfAsync(uid))!;
        }

        var now = Clock.UtcNow();
        var photo = PlayerRules.Clean(r.PhotoUrl, 2000) ?? user.PhotoUrl;

        user.DisplayName = name;
        user.Gender = r.Gender;
        user.DateOfBirth = r.DateOfBirth;
        user.City = PlayerRules.Clean(r.City, 80);
        user.PhotoUrl = photo;
        user.IsDiscoverable = r.IsDiscoverable;
        user.UpdatedAt = now;

        self.Name = name;
        self.Gender = r.Gender;
        self.DateOfBirth = r.DateOfBirth;
        self.City = user.City;
        self.PhoneNumber = PlayerRules.Clean(r.PhoneNumber, 20);
        self.PhotoUrl = photo;
        self.Role = r.Role;
        self.BattingStyle = r.BattingStyle;
        self.BowlingStyle = r.BowlingStyle;
        self.HeightCm = r.HeightCm;
        self.WeightKg = r.WeightKg;
        self.JerseyNumber = r.JerseyNumber;
        self.DeletedAt = null;
        self.UpdatedAt = now;

        await _users.SaveChangesAsync();          // same scoped DbContext: saves the user and the player together
        await MirrorAsync(user, self);
        return ToDto(user, self);
    }

    public async Task SyncSelfFromUserAsync(User user)
    {
        var self = await _players.GetSelfAsync(user.Id);
        if (self == null) return;
        var name = DisplayNameOf(user);
        if (self.Name == name && self.PhotoUrl == user.PhotoUrl) return;
        self.Name = name;
        self.PhotoUrl = user.PhotoUrl;
        self.UpdatedAt = Clock.UtcNow();
        await _players.SaveAsync(self, false);
        await MirrorAsync(user, self);
    }

    private static string DisplayNameOf(User u)
    {
        if (!string.IsNullOrWhiteSpace(u.DisplayName)) return u.DisplayName.Trim();
        var local = u.Email?.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "Me" : local;
    }

    private static ProfileDto ToDto(User u, Player p) => new()
    {
        UserId = u.Id,
        PlayerId = p.Id,
        Email = u.Email,
        PhoneNumber = p.PhoneNumber ?? u.PhoneNumber,
        PhotoUrl = u.PhotoUrl,
        DisplayName = u.DisplayName ?? p.Name,
        Gender = u.Gender,
        DateOfBirth = u.DateOfBirth,
        City = u.City,
        IsDiscoverable = u.IsDiscoverable,
        Role = p.Role,
        BattingStyle = p.BattingStyle,
        BowlingStyle = p.BowlingStyle,
        HeightCm = p.HeightCm,
        WeightKg = p.WeightKg,
        JerseyNumber = p.JerseyNumber,
        UpdatedAt = p.UpdatedAt
    };
}
