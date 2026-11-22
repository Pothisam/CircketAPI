using Cricket_Models.CommonModels;
using Cricket_Models.ConfigurationModels;
using Cricket_Models.Enums;
using Cricket_Models.MatchModels;
using Cricket_Repository.Entity;
using Cricket_Repository.MatchRepository;
using Cricket_Services.NotificationServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cricket_Services.MatchServices;

public interface IMatchService
{
    // CRUD
    Task<ListResponse<MatchSummaryDto>> ListSummariesAsync(string uid, string? status, DateTime? updatedSince, int? limit, string? cursor);
    Task<ListResponse<MatchDto>> ListFullAsync(string uid, string? status, DateTime? updatedSince, int? limit, string? cursor);
    Task<List<MatchDto>> ListChangedAsync(string uid, DateTime? since, bool includeDeleted);   // for sync
    Task<MatchDto> GetAsync(string uid, string id);
    Task<MatchDto> UpsertAsync(string uid, string id, MatchDto dto);
    Task DeleteAsync(string uid, string id);

    // Live scoring
    Task<(InningsDto Innings, bool Created)> StartInningsAsync(string uid, string matchId, StartInningsRequest request);
    Task<InningsDto> PatchInningsAsync(string uid, string matchId, int inningsNo, JsonElement body);
    Task<(AddBallResponse Result, bool Created)> AddBallAsync(string uid, string matchId, int inningsNo, BallDto ball);
    Task<UndoBallResponse> UndoLastBallAsync(string uid, string matchId, int inningsNo);
    Task<MatchDto> CompleteAsync(string uid, string matchId, CompleteMatchRequest request);

    // Sharing / following
    Task<ShareResponse> ShareAsync(string uid, string matchId, ShareRequest request, string baseUrl);
    Task UnshareAsync(string uid, string matchId);
    Task<MatchDto> GetPublicAsync(string shareCode);
    Task<MatchSummaryDto> FollowAsync(string followerUid, string shareCode);
    Task UnfollowAsync(string followerUid, string ownerId, string matchId);
    Task<List<MatchSummaryDto>> ListFollowingAsync(string followerUid);
}

public class MatchService : IMatchService
{
    private const string ShareAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O/1/I
    private readonly IMatchRepo _matches;
    private readonly INotificationDispatcher _dispatcher;

    public MatchService(IMatchRepo matches, INotificationDispatcher dispatcher)
    {
        _matches = matches;
        _dispatcher = dispatcher;
    }

    // =================================================================== CRUD

    public async Task<ListResponse<MatchSummaryDto>> ListSummariesAsync(string uid, string? status, DateTime? updatedSince, int? limit, string? cursor)
    {
        var (l, offset) = ParseList(limit, cursor, status);
        var rows = await _matches.ListAsync(uid, status, updatedSince, false, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        var innings = await _matches.GetInningsForMatchesAsync(page.Items);
        return new ListResponse<MatchSummaryDto>
        {
            Items = page.Items.Select(m => MatchMapper.ToSummary(m, innings.Where(i => i.UserId == m.UserId && i.MatchId == m.Id))).ToList(),
            NextCursor = page.NextCursor
        };
    }

    public async Task<ListResponse<MatchDto>> ListFullAsync(string uid, string? status, DateTime? updatedSince, int? limit, string? cursor)
    {
        var (l, offset) = ParseList(limit, cursor, status);
        var rows = await _matches.ListAsync(uid, status, updatedSince, false, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        var aggregates = await _matches.GetAggregatesAsync(page.Items);
        return new ListResponse<MatchDto> { Items = aggregates.Select(MatchMapper.ToDto).ToList(), NextCursor = page.NextCursor };
    }

    public async Task<List<MatchDto>> ListChangedAsync(string uid, DateTime? since, bool includeDeleted)
    {
        var rows = await _matches.ListAsync(uid, null, since, includeDeleted, 0, 100000);
        var live = rows.Where(r => r.DeletedAt == null).ToList();
        var aggregates = await _matches.GetAggregatesAsync(live);
        // deleted matches come back without children (only their id/deletedAt matter to the client)
        var deleted = rows.Where(r => r.DeletedAt != null).Select(r => new MatchAggregate { Match = r });
        return aggregates.Concat(deleted).Select(MatchMapper.ToDto).ToList();
    }

    public async Task<MatchDto> GetAsync(string uid, string id)
    {
        var agg = await _matches.GetAggregateAsync(uid, id);
        if (agg == null || agg.Match.DeletedAt != null) throw ApiException.NotFound("Match");
        return MatchMapper.ToDto(agg);
    }

    public async Task<MatchDto> UpsertAsync(string uid, string id, MatchDto dto)
    {
        dto.Innings ??= new();
        dto.TeamAPlayers ??= new();
        dto.TeamBPlayers ??= new();
        dto.PlayerNames ??= new();
        var v = new ValidationBuilder();
        MatchMapper.ValidateMatch(dto, v);
        v.ThrowIfAny();

        var now = Clock.UtcNow();
        var existing = await _matches.GetAsync(uid, id);
        if (existing != null && dto.Version.HasValue && dto.Version.Value < existing.Version)
        {
            var current = await _matches.GetAggregateAsync(uid, id);
            throw ApiException.Conflict("Version conflict: the server has a newer match",
                new() { ["current"] = current == null ? null : MatchMapper.ToDto(current) });
        }

        var isNew = existing == null;
        var m = existing ?? new Match { Id = id, UserId = uid, CreatedAt = now };
        m.TeamAId = dto.TeamAId;
        m.TeamBId = dto.TeamBId;
        m.TeamAName = dto.TeamAName.Trim();
        m.TeamBName = dto.TeamBName.Trim();
        m.TeamAShort = dto.TeamAShort.Trim();
        m.TeamBShort = dto.TeamBShort.Trim();
        m.Overs = dto.Overs;
        m.TossWinnerId = dto.TossWinnerId;
        m.ElectedToBat = dto.ElectedToBat;
        m.MatchDate = MatchMapper.ToUtc(dto.Date);
        m.Venue = dto.Venue ?? "";
        m.Status = dto.Status;
        m.WinnerId = MatchMapper.NullIfEmpty(dto.WinnerId);
        m.ResultText = dto.ResultText ?? "";
        m.Version = isNew ? 1 : m.Version + 1;
        m.UpdatedAt = now;
        m.DeletedAt = null;                                  // an upsert revives a soft-deleted match

        var agg = new MatchAggregate { Match = m };
        for (var i = 0; i < dto.TeamAPlayers.Count; i++) agg.Players.Add(NewMatchPlayer(uid, id, dto.TeamAPlayers[i], m.TeamAId, dto.PlayerNames, i));
        for (var i = 0; i < dto.TeamBPlayers.Count; i++) agg.Players.Add(NewMatchPlayer(uid, id, dto.TeamBPlayers[i], m.TeamBId, dto.PlayerNames, i));

        for (var i = 0; i < dto.Innings.Count; i++)
        {
            var d = dto.Innings[i];
            var innings = new Innings
            {
                UserId = uid, MatchId = id, InningsNo = i + 1,
                BattingTeamId = d.BattingTeamId, BowlingTeamId = d.BowlingTeamId,
                StrikerId = MatchMapper.NullIfEmpty(d.Striker),
                NonStrikerId = MatchMapper.NullIfEmpty(d.NonStriker),
                BowlerId = MatchMapper.NullIfEmpty(d.Bowler),
                Completed = d.Completed
            };
            var balls = (d.Balls ?? new()).Select((b, seq) => MatchMapper.ToEntity(uid, id, i + 1, seq, b, now)).ToList();
            MatchMapper.ApplyTotals(innings, balls);
            agg.Innings.Add(innings);
            agg.Balls.AddRange(balls);
        }

        await _matches.SaveAggregateAsync(agg, isNew);
        var result = MatchMapper.ToDto(agg);
        result.Visibility = existing?.Visibility ?? "private";
        return result;
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var m = await _matches.GetAsync(uid, id);
        if (m == null || m.DeletedAt != null) throw ApiException.NotFound("Match");
        await _matches.SoftDeleteAsync(m, Clock.UtcNow());
    }

    // ============================================================ live scoring

    public async Task<(InningsDto Innings, bool Created)> StartInningsAsync(string uid, string matchId, StartInningsRequest r)
    {
        var match = await RequireMatchAsync(uid, matchId);
        var teams = new HashSet<string> { match.TeamAId, match.TeamBId };
        new ValidationBuilder()
            .Check(teams.Contains(r.BattingTeamId), "battingTeamId", "must be one of the match's teams")
            .Check(teams.Contains(r.BowlingTeamId) && r.BowlingTeamId != r.BattingTeamId, "bowlingTeamId", "must be the other team")
            .ThrowIfAny();

        var existing = await _matches.GetInningsAsync(uid, matchId, r.InningsNo);
        if (existing != null)
        {
            // Retry-safe: same request returns the existing innings; a different one is a conflict.
            if (existing.BattingTeamId == r.BattingTeamId && existing.BowlingTeamId == r.BowlingTeamId)
                return (MatchMapper.ToDto(existing), false);
            throw ApiException.Conflict($"Innings {r.InningsNo} already exists with different teams");
        }

        await _matches.AddInningsAsync(new Innings
        {
            UserId = uid, MatchId = matchId, InningsNo = r.InningsNo,
            BattingTeamId = r.BattingTeamId, BowlingTeamId = r.BowlingTeamId
        });

        if (r.InningsNo == 1) match.Status = "live";
        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();

        if (r.InningsNo == 1)
            _dispatcher.Enqueue(sp => sp.GetRequiredService<IMatchEventNotifier>().MatchStartedAsync(uid, matchId));

        var created = await _matches.GetInningsAsync(uid, matchId, r.InningsNo);
        return (MatchMapper.ToDto(created!), true);
    }

    /// <summary>PATCH with "present vs absent" semantics: an explicit null clears the field.</summary>
    public async Task<InningsDto> PatchInningsAsync(string uid, string matchId, int inningsNo, JsonElement body)
    {
        var match = await RequireMatchAsync(uid, matchId);
        var innings = await _matches.GetInningsAsync(uid, matchId, inningsNo) ?? throw ApiException.NotFound("Innings");
        if (body.ValueKind != JsonValueKind.Object) throw ApiException.Validation("Body must be a JSON object");

        var wasCompleted = innings.Completed;
        innings.StrikerId = ReadNullableString(body, "striker", innings.StrikerId);
        innings.NonStrikerId = ReadNullableString(body, "nonStriker", innings.NonStrikerId);
        innings.BowlerId = ReadNullableString(body, "bowler", innings.BowlerId);
        if (body.TryGetProperty("completed", out var c))
        {
            if (c.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw ApiException.Validation("completed must be a boolean", new List<ErrorDetail> { new("completed", "must be a boolean") });
            innings.Completed = c.GetBoolean();
        }

        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();

        if (!wasCompleted && innings.Completed)
            _dispatcher.Enqueue(sp => sp.GetRequiredService<IMatchEventNotifier>().InningsCompletedAsync(uid, matchId, inningsNo));

        return MatchMapper.ToDto(innings);
    }

    public async Task<(AddBallResponse Result, bool Created)> AddBallAsync(string uid, string matchId, int inningsNo, BallDto ball)
    {
        var match = await RequireMatchAsync(uid, matchId);
        var innings = await _matches.GetInningsAsync(uid, matchId, inningsNo) ?? throw ApiException.NotFound("Innings");

        var members = await _matches.GetMatchPlayerIdsAsync(uid, matchId);
        var v = new ValidationBuilder();
        MatchMapper.ValidateBall(ball, "ball", members, v);
        v.ThrowIfAny();

        var now = Clock.UtcNow();
        var count = await _matches.GetBallCountAsync(uid, matchId, inningsNo);
        var seq = ball.Seq ?? count;
        var entity = MatchMapper.ToEntity(uid, matchId, inningsNo, seq, ball, now);

        if (seq < count)
        {
            // Retry of a ball we already stored? identical -> 200, different -> 409.
            var stored = await _matches.GetBallAsync(uid, matchId, inningsNo, seq);
            if (stored != null && MatchMapper.SameBall(stored, entity))
                return (BuildBallResponse(stored, innings, match), false);
            throw ApiException.Conflict($"A different ball already exists at seq {seq}", new() { ["expectedSeq"] = count });
        }
        if (seq > count)
            throw ApiException.Conflict($"Ball sequence gap: expected seq {count}", new() { ["expectedSeq"] = count });

        try
        {
            await _matches.AddBallAsync(entity);
        }
        catch (DbUpdateException)
        {
            // Two requests raced for the same seq.
            throw ApiException.Conflict("Another ball was recorded at this position", new() { ["expectedSeq"] = count + 1 });
        }

        await _matches.RecomputeTotalsAsync(innings);
        match.UpdatedAt = now;
        await _matches.SaveChangesAsync();

        _dispatcher.Enqueue(sp => sp.GetRequiredService<IMatchEventNotifier>().BallRecordedAsync(uid, matchId, inningsNo, seq));
        return (BuildBallResponse(entity, innings, match), true);
    }

    public async Task<UndoBallResponse> UndoLastBallAsync(string uid, string matchId, int inningsNo)
    {
        var match = await RequireMatchAsync(uid, matchId);
        var innings = await _matches.GetInningsAsync(uid, matchId, inningsNo) ?? throw ApiException.NotFound("Innings");
        var removed = await _matches.DeleteLastBallAsync(uid, matchId, inningsNo);
        if (removed == null) throw ApiException.NotFound("Ball");

        await _matches.RecomputeTotalsAsync(innings);
        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();
        return new UndoBallResponse { Innings = MatchMapper.ToTotals(innings) };   // undo never notifies
    }

    public async Task<MatchDto> CompleteAsync(string uid, string matchId, CompleteMatchRequest r)
    {
        var match = await RequireMatchAsync(uid, matchId);
        var winner = MatchMapper.NullIfEmpty(r.WinnerId);
        new ValidationBuilder()
            .OneOf(r.Status, new HashSet<string> { "completed", "abandoned" }, "status")
            .Check(winner == null || winner == match.TeamAId || winner == match.TeamBId, "winnerId", "must be one of the match's teams or null")
            .Check(!(r.Status == "abandoned" && winner != null), "winnerId", "must be null for an abandoned match")
            .ThrowIfAny();

        var wasLive = match.Status == "live";
        match.Status = r.Status;
        match.WinnerId = winner;
        match.ResultText = r.ResultText ?? "";
        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();

        // Only the live -> finished transition notifies, so a retried request doesn't push twice.
        if (wasLive)
            _dispatcher.Enqueue(sp => sp.GetRequiredService<IMatchEventNotifier>().MatchCompletedAsync(uid, matchId));

        return await GetAsync(uid, matchId);
    }

    // =========================================================== sharing

    public async Task<ShareResponse> ShareAsync(string uid, string matchId, ShareRequest request, string baseUrl)
    {
        var visibility = request.Visibility ?? "link";
        new ValidationBuilder().OneOf(visibility, new HashSet<string> { "link", "public" }, "visibility").ThrowIfAny();

        var match = await RequireMatchAsync(uid, matchId);
        if (string.IsNullOrEmpty(match.ShareCode)) match.ShareCode = await NewShareCodeAsync();
        match.Visibility = visibility;
        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();
        return new ShareResponse { ShareCode = match.ShareCode, Url = $"{baseUrl.TrimEnd('/')}/m/{match.ShareCode}" };
    }

    public async Task UnshareAsync(string uid, string matchId)
    {
        var match = await RequireMatchAsync(uid, matchId);
        match.Visibility = "private";
        match.ShareCode = null;                        // revokes existing links
        match.UpdatedAt = Clock.UtcNow();
        await _matches.SaveChangesAsync();
    }

    public async Task<MatchDto> GetPublicAsync(string shareCode)
    {
        var match = await _matches.GetByShareCodeAsync(shareCode.Trim().ToUpperInvariant()) ?? throw ApiException.NotFound("Match");
        var agg = await _matches.GetAggregateAsync(match.UserId, match.Id) ?? throw ApiException.NotFound("Match");
        return MatchMapper.ToDto(agg);
    }

    public async Task<MatchSummaryDto> FollowAsync(string followerUid, string shareCode)
    {
        var match = await _matches.GetByShareCodeAsync(shareCode.Trim().ToUpperInvariant()) ?? throw ApiException.NotFound("Match");
        await _matches.AddFollowerAsync(match.UserId, match.Id, followerUid);
        var innings = await _matches.GetInningsForMatchesAsync(new List<Match> { match });
        return MatchMapper.ToSummary(match, innings);
    }

    public async Task UnfollowAsync(string followerUid, string ownerId, string matchId)
    {
        if (!await _matches.RemoveFollowerAsync(ownerId, matchId, followerUid)) throw ApiException.NotFound("Follow");
    }

    public async Task<List<MatchSummaryDto>> ListFollowingAsync(string followerUid)
    {
        var matches = await _matches.ListFollowedAsync(followerUid);
        var innings = await _matches.GetInningsForMatchesAsync(matches);
        return matches.Select(m => MatchMapper.ToSummary(m, innings.Where(i => i.UserId == m.UserId && i.MatchId == m.Id))).ToList();
    }

    // ============================================================ helpers

    private async Task<Match> RequireMatchAsync(string uid, string matchId)
    {
        var m = await _matches.GetAsync(uid, matchId);
        if (m == null || m.DeletedAt != null) throw ApiException.NotFound("Match");
        return m;
    }

    private static (int Limit, int Offset) ParseList(int? limit, string? cursor, string? status)
    {
        if (status != null && !CricketEnums.MatchStatuses.Contains(status))
            throw ApiException.Validation("status must be live, completed or abandoned", new List<ErrorDetail> { new("status", "invalid") });
        return Paging.Parse(limit, cursor);
    }

    private static MatchPlayer NewMatchPlayer(string uid, string matchId, string playerId, string teamId, Dictionary<string, string> names, int order)
    {
        var name = names.TryGetValue(playerId, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : playerId;
        return new MatchPlayer
        {
            UserId = uid, MatchId = matchId, PlayerId = playerId, TeamId = teamId,
            PlayerName = name.Length > 120 ? name[..120] : name, BattingOrder = order
        };
    }

    private static AddBallResponse BuildBallResponse(Ball ball, Innings innings, Match match) => new()
    {
        Ball = MatchMapper.ToDto(ball, includeSeq: true),
        Innings = MatchMapper.ToTotals(innings),
        Match = new MatchStateDto { Status = match.Status, ResultText = match.ResultText }
    };

    private static string? ReadNullableString(JsonElement body, string name, string? current)
    {
        if (!body.TryGetProperty(name, out var p)) return current;      // absent -> unchanged
        return p.ValueKind switch
        {
            JsonValueKind.Null => null,                                 // explicit null -> cleared
            JsonValueKind.String => MatchMapper.NullIfEmpty(p.GetString()),
            _ => throw ApiException.Validation($"{name} must be a string or null", new List<ErrorDetail> { new(name, "must be a string or null") })
        };
    }

    private async Task<string> NewShareCodeAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = string.Create(6, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = ShareAlphabet[RandomNumberGenerator.GetInt32(ShareAlphabet.Length)];
            });
            if (!await _matches.ShareCodeExistsAsync(code)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique share code");
    }
}
