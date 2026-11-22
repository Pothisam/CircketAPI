using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.PlayerModels;
using Cricket_Repository.Entity;
using Cricket_Repository.PlayerRepository;
using Cricket_Repository.UserRepository;

namespace Cricket_Services.PlayerServices;

public interface IPlayerService
{
    Task<ListResponse<PlayerDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted);
    Task<List<PlayerDto>> ListChangedAsync(string uid, DateTime? since);       // for sync (includes soft-deleted)
    Task<PlayerDto> GetAsync(string uid, string id);
    Task<PlayerDto> UpsertAsync(string uid, string id, PlayerDto dto);
    Task DeleteAsync(string uid, string id);

    /// <summary>Search the caller's own players (name / e-mail) and registered users (name, or exact e-mail).</summary>
    Task<List<PlayerSearchItem>> SearchAsync(string uid, string? q, int? limit);
    /// <summary>Adds a registered user to the caller's player list (linked to that user). Returns (player, created).</summary>
    Task<(PlayerDto Player, bool Created)> AddFromUserAsync(string uid, string userId);
}

public class PlayerService : IPlayerService
{
    private readonly IPlayerRepo _players;
    private readonly IUserRepo _users;

    public PlayerService(IPlayerRepo players, IUserRepo users)
    {
        _players = players;
        _users = users;
    }

    public async Task<ListResponse<PlayerDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted)
    {
        var (l, offset) = Paging.Parse(limit, cursor);
        var rows = await _players.ListAsync(uid, updatedSince, includeDeleted, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        return new ListResponse<PlayerDto> { Items = page.Items.Select(ToDto).ToList(), NextCursor = page.NextCursor };
    }

    public async Task<List<PlayerDto>> ListChangedAsync(string uid, DateTime? since) =>
        (await _players.ListAsync(uid, since, true, 0, 100000)).Select(ToDto).ToList();

    public async Task<PlayerDto> GetAsync(string uid, string id)
    {
        var p = await _players.GetAsync(uid, id);
        if (p == null || p.DeletedAt != null) throw ApiException.NotFound("Player");
        return ToDto(p);
    }

    public async Task<PlayerDto> UpsertAsync(string uid, string id, PlayerDto dto)
    {
        var name = dto.Name?.Trim() ?? "";
        var email = PlayerRules.NormalizeEmail(dto.Email);
        new ValidationBuilder()
            .Check(name.Length is > 0 and <= 120, "name", "required, max 120 characters")
            .OneOf(dto.Role, CricketEnums.PlayerRoles, "role")
            .OneOf(dto.BattingStyle, CricketEnums.BattingStyles, "battingStyle")
            .OneOf(dto.BowlingStyle, CricketEnums.BowlingStyles, "bowlingStyle")
            .Check(email == null || PlayerRules.LooksLikeEmail(email), "email", "is not a valid e-mail address")
            .CheckProfile(dto.Gender, dto.DateOfBirth, dto.HeightCm, dto.WeightKg, dto.JerseyNumber)
            .ThrowIfAny();

        var now = Clock.UtcNow();
        var player = await _players.GetAsync(uid, id);
        var isNew = player == null;

        // A player that IS a registered user (added by search / matched by e-mail) belongs to that user.
        // Only they can change it (through their own profile); everyone else may just remove it from their list.
        if (player is { IsSelf: false, LinkedUserId: not null })
            throw ApiException.Forbidden("This player is a registered user. Only they can edit their profile.");

        player ??= new Player { Id = id, UserId = uid, CreatedAt = now };

        if (player.IsSelf)
        {
            // The owner's own player: e-mail and links are managed by the server (they come from the sign-in).
            email = player.Email;
        }
        else if (email != null)
        {
            var self = await _players.GetSelfAsync(uid);
            if (self?.Email == email)
                throw ApiException.Validation("That is your own e-mail; your profile already is a player in your list.",
                    new List<ErrorDetail> { new("email", "belongs to you") });
            if (await _players.FindByEmailAsync(uid, email, id) != null)
                throw ApiException.Conflict("Another player in your list already uses this e-mail");
        }

        player.Name = name;
        player.Role = dto.Role;
        player.BattingStyle = dto.BattingStyle;
        player.BowlingStyle = dto.BowlingStyle;
        player.Email = email;
        player.PhoneNumber = PlayerRules.Clean(dto.PhoneNumber, 20);
        player.Gender = dto.Gender;
        player.DateOfBirth = dto.DateOfBirth;
        player.HeightCm = dto.HeightCm;
        player.WeightKg = dto.WeightKg;
        player.JerseyNumber = dto.JerseyNumber;
        player.City = PlayerRules.Clean(dto.City, 80);
        player.PhotoUrl = PlayerRules.Clean(dto.PhotoUrl, 2000);
        player.UpdatedAt = now;
        player.DeletedAt = null;

        // A manual player whose e-mail belongs to a registered user IS that user.
        if (!player.IsSelf)
        {
            player.LinkedUserId = email == null ? null : await _players.FindUserIdByEmailAsync(email);
            if (player.LinkedUserId != null && await _players.GetUserWithSelfAsync(player.LinkedUserId) is { } hit)
                ApplyRegisteredProfile(player, hit);
        }

        await _players.SaveAsync(player, isNew);

        if (player.IsSelf) await CopySelfToUserAsync(uid, player);
        return ToDto(player);
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var p = await _players.GetAsync(uid, id);
        if (p == null || p.DeletedAt != null) throw ApiException.NotFound("Player");
        if (p.IsSelf) throw ApiException.Validation("Your own profile cannot be deleted (delete the account instead).");
        await _players.SoftDeleteAsync(p, Clock.UtcNow());
    }

    // ------------------------------------------------------------------- search

    public async Task<List<PlayerSearchItem>> SearchAsync(string uid, string? q, int? limit)
    {
        var term = q?.Trim() ?? "";
        new ValidationBuilder()
            .Check(term.Length is >= 2 and <= 100, "q", "must be 2 to 100 characters")
            .ThrowIfAny();
        var take = Math.Clamp(limit ?? 20, 1, 50);

        var result = new List<PlayerSearchItem>();
        foreach (var p in await _players.SearchOwnAsync(uid, term, take))
        {
            result.Add(new PlayerSearchItem
            {
                Source = "mine",
                PlayerId = p.Id,
                UserId = p.LinkedUserId,
                Name = p.Name,
                Email = p.Email,
                PhotoUrl = p.PhotoUrl,
                Role = p.Role,
                BattingStyle = p.BattingStyle,
                BowlingStyle = p.BowlingStyle,
                City = p.City,
                IsRegistered = p.LinkedUserId != null,
                IsSelf = p.IsSelf
            });
        }

        // Other people are found by name, or by their exact e-mail (never by a partial e-mail, so the
        // user list cannot be enumerated).
        var exactEmail = term.Contains('@');
        var emailTerm = exactEmail ? PlayerRules.NormalizeEmail(term)! : term;
        foreach (var hit in await _players.SearchUsersAsync(uid, emailTerm, exactEmail, take))
        {
            var u = hit.User;
            result.Add(new PlayerSearchItem
            {
                Source = "user",
                UserId = u.Id,
                Name = hit.Self?.Name ?? u.DisplayName ?? "Player",
                Email = exactEmail ? u.Email : PlayerRules.MaskEmail(u.Email),
                PhotoUrl = u.PhotoUrl,
                Role = hit.Self?.Role ?? "batter",
                BattingStyle = hit.Self?.BattingStyle ?? "Right-hand bat",
                BowlingStyle = hit.Self?.BowlingStyle ?? "None",
                City = u.City,
                IsRegistered = true
            });
        }
        return result;
    }

    public async Task<(PlayerDto Player, bool Created)> AddFromUserAsync(string uid, string userId)
    {
        if (userId == uid)
            throw ApiException.Validation("That is you; your profile is already in your player list.",
                new List<ErrorDetail> { new("userId", "is yourself") });

        var hit = await _players.GetDiscoverableUserAsync(userId) ?? throw ApiException.NotFound("User");
        var now = Clock.UtcNow();

        var existing = await _players.GetByLinkedUserAsync(uid, userId);
        if (existing != null)
        {
            if (existing.DeletedAt == null) return (ToDto(existing), false);
            existing.DeletedAt = null;                       // they were removed earlier: bring them back
            existing.UpdatedAt = now;
            await _players.SaveAsync(existing, false);
            return (ToDto(existing), true);
        }

        var u = hit.User;
        var self = hit.Self;
        var player = new Player
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = uid,
            Name = self?.Name ?? u.DisplayName ?? u.Email?.Split('@')[0] ?? "Player",
            Email = null,   // the e-mail was masked in the search result, so it is not copied either
            // Only the playing details are copied. Height, weight, date of birth and phone stay private.
            Role = self?.Role ?? "batter",
            BattingStyle = self?.BattingStyle ?? "Right-hand bat",
            BowlingStyle = self?.BowlingStyle ?? "None",
            City = u.City,
            PhotoUrl = u.PhotoUrl,
            LinkedUserId = u.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _players.SaveAsync(player, true);
        return (ToDto(player), true);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The owner's player is the source of the profile; keep the user row (identity) in step.</summary>
    private async Task CopySelfToUserAsync(string uid, Player self)
    {
        var user = await _users.GetUserAsync(uid);
        if (user == null) return;
        user.DisplayName = self.Name;
        user.Gender = self.Gender;
        user.DateOfBirth = self.DateOfBirth;
        user.City = self.City;
        user.PhotoUrl = self.PhotoUrl ?? user.PhotoUrl;
        user.UpdatedAt = Clock.UtcNow();
        await _users.SaveChangesAsync();
        await _players.MirrorProfileAsync(uid, self.Name, self.Role, self.BattingStyle, self.BowlingStyle, user.City, user.PhotoUrl);
    }

    /// <summary>Takes the public profile of the registered user and drops the private details typed by hand.</summary>
    private static void ApplyRegisteredProfile(Player copy, UserSearchHit hit)
    {
        var u = hit.User;
        var self = hit.Self;
        copy.Name = self?.Name ?? u.DisplayName ?? copy.Name;
        copy.Role = self?.Role ?? copy.Role;
        copy.BattingStyle = self?.BattingStyle ?? copy.BattingStyle;
        copy.BowlingStyle = self?.BowlingStyle ?? copy.BowlingStyle;
        copy.City = u.City;
        copy.PhotoUrl = u.PhotoUrl;
        copy.PhoneNumber = null;
        copy.Gender = null;
        copy.DateOfBirth = null;
        copy.HeightCm = null;
        copy.WeightKg = null;
        copy.JerseyNumber = null;
    }

    public static PlayerDto ToDto(Player p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Role = p.Role,
        BattingStyle = p.BattingStyle,
        BowlingStyle = p.BowlingStyle,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        DeletedAt = p.DeletedAt,
        Email = p.Email,
        PhoneNumber = p.PhoneNumber,
        Gender = p.Gender,
        DateOfBirth = p.DateOfBirth,
        HeightCm = p.HeightCm,
        WeightKg = p.WeightKg,
        JerseyNumber = p.JerseyNumber,
        City = p.City,
        PhotoUrl = p.PhotoUrl,
        LinkedUserId = p.LinkedUserId,
        IsSelf = p.IsSelf,
        IsRegistered = p.LinkedUserId != null
    };
}
