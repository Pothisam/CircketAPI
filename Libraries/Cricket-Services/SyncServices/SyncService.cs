using Cricket_Models.CommonModels;
using Cricket_Models.SyncModels;
using Cricket_Services.MatchServices;
using Cricket_Services.PlayerServices;
using Cricket_Services.TeamServices;

namespace Cricket_Services.SyncServices;

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(string uid, SyncPushRequest request);
    Task<SyncPullResponse> PullAsync(string uid, DateTime? since);
}

public class SyncService : ISyncService
{
    private readonly ITeamService _teams;
    private readonly IPlayerService _players;
    private readonly IMatchService _matches;

    public SyncService(ITeamService teams, IPlayerService players, IMatchService matches)
    {
        _teams = teams;
        _players = players;
        _matches = matches;
    }

    /// <summary>
    /// Order matters: players first (teams reference them), then teams, then matches, then deletes.
    /// Each item succeeds or is rejected on its own, so one bad row never blocks the rest.
    /// </summary>
    public async Task<SyncPushResponse> PushAsync(string uid, SyncPushRequest request)
    {
        var response = new SyncPushResponse();
        request.Deleted ??= new SyncDeleted();

        foreach (var p in request.Players ?? new())
        {
            p.Id = string.IsNullOrWhiteSpace(p.Id) ? NewId() : p.Id;
            await Try(response, "player", p.Id, () => _players.UpsertAsync(uid, p.Id, p), c => c.Players++);
        }
        foreach (var t in request.Teams ?? new())
        {
            t.Id = string.IsNullOrWhiteSpace(t.Id) ? NewId() : t.Id;
            await Try(response, "team", t.Id, () => _teams.UpsertAsync(uid, t.Id, t), c => c.Teams++);
        }
        foreach (var m in request.Matches ?? new())
        {
            m.Id = string.IsNullOrWhiteSpace(m.Id) ? NewId() : m.Id;
            await Try(response, "match", m.Id, () => _matches.UpsertAsync(uid, m.Id, m), c => c.Matches++);
        }

        // Deletes are idempotent: an id that is already gone counts as accepted.
        foreach (var id in request.Deleted.Players) await Try(response, "player", id, () => _players.DeleteAsync(uid, id), c => c.Players++, true);
        foreach (var id in request.Deleted.Teams) await Try(response, "team", id, () => _teams.DeleteAsync(uid, id), c => c.Teams++, true);
        foreach (var id in request.Deleted.Matches) await Try(response, "match", id, () => _matches.DeleteAsync(uid, id), c => c.Matches++, true);

        response.ServerTime = Clock.UtcNow();
        return response;
    }

    public async Task<SyncPullResponse> PullAsync(string uid, DateTime? since)
    {
        // Captured before reading: anything written while we read is simply sent again on the next pull.
        var serverTime = Clock.UtcNow();
        var response = new SyncPullResponse { ServerTime = serverTime };

        var teams = await _teams.ListChangedAsync(uid, since);
        response.Teams = teams.Where(t => t.DeletedAt == null).ToList();
        response.Deleted.Teams = teams.Where(t => t.DeletedAt != null).Select(t => t.Id).ToList();

        var players = await _players.ListChangedAsync(uid, since);
        response.Players = players.Where(p => p.DeletedAt == null).ToList();
        response.Deleted.Players = players.Where(p => p.DeletedAt != null).Select(p => p.Id).ToList();

        var matches = await _matches.ListChangedAsync(uid, since, includeDeleted: true);
        response.Matches = matches.Where(m => m.DeletedAt == null).ToList();
        response.Deleted.Matches = matches.Where(m => m.DeletedAt != null).Select(m => m.Id).ToList();
        return response;
    }

    private static async Task Try(SyncPushResponse response, string type, string id, Func<Task> work, Action<SyncCounts> count, bool ignoreNotFound = false)
    {
        try
        {
            await work();
            count(response.Accepted);
        }
        catch (ApiException ex) when (ex.StatusCode == 404 && ignoreNotFound)
        {
            count(response.Accepted);
        }
        catch (ApiException ex)
        {
            response.Rejected.Add(new SyncRejected { Type = type, Id = id, Reason = ex.Code, Message = ex.Message });
        }
    }

    private static async Task Try<T>(SyncPushResponse response, string type, string id, Func<Task<T>> work, Action<SyncCounts> count) =>
        await Try(response, type, id, (Func<Task>)(async () => { await work(); }), count);

    private static string NewId() => Guid.NewGuid().ToString("N");
}
