using Cricket_Models.Enums;
using Cricket_Repository.Entity;
using Cricket_Repository.MatchRepository;
using Microsoft.Extensions.Caching.Memory;

namespace Cricket_Services.NotificationServices;

/// <summary>Builds the live-match pushes (spec section 7.2). Always called from the background dispatcher.</summary>
public interface IMatchEventNotifier
{
    Task MatchStartedAsync(string ownerId, string matchId);
    Task BallRecordedAsync(string ownerId, string matchId, int inningsNo, int seq);
    Task InningsCompletedAsync(string ownerId, string matchId, int inningsNo);
    Task MatchCompletedAsync(string ownerId, string matchId);
}

public class MatchEventNotifier : IMatchEventNotifier
{
    private readonly IMatchRepo _matches;
    private readonly INotificationService _notifications;
    private readonly IMemoryCache _cache;

    public MatchEventNotifier(IMatchRepo matches, INotificationService notifications, IMemoryCache cache)
    {
        _matches = matches;
        _notifications = notifications;
        _cache = cache;
    }

    public async Task MatchStartedAsync(string ownerId, string matchId)
    {
        var m = await _matches.GetAsync(ownerId, matchId);
        if (m == null) return;
        await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
        {
            Type = NotificationTypes.MatchStart,
            Title = "Match started",
            Body = $"{m.TeamAShort} vs {m.TeamBShort} ({m.Overs} overs)",
            Preference = p => p.MatchStart,
            Data = Data(matchId, null)
        });
    }

    public async Task BallRecordedAsync(string ownerId, string matchId, int inningsNo, int seq)
    {
        var match = await _matches.GetAsync(ownerId, matchId);
        var innings = await _matches.GetInningsAsync(ownerId, matchId, inningsNo);
        var ball = await _matches.GetBallAsync(ownerId, matchId, inningsNo, seq);
        if (match == null || innings == null || ball == null) return;

        var names = await _matches.GetPlayerNamesAsync(ownerId, matchId);
        string Name(string? id) => id != null && names.TryGetValue(id, out var n) ? n : "Batter";
        // Notifications are sent from a queue, so the scorer may already have recorded later balls:
        // report the score as it was at THIS ball, not the innings' current totals.
        var snap = await _matches.GetScoreUpToAsync(ownerId, matchId, inningsNo, seq);
        var score = ScoreText(match, innings.BattingTeamId, snap.Runs, snap.Wickets, snap.LegalBalls);
        var data = Data(matchId, inningsNo);

        // Four / six: at most one boundary push per over per match, so a big over doesn't flood devices.
        if (ball.Extra == "none" && (ball.Runs == 4 || ball.Runs == 6))
        {
            var key = $"boundary:{ownerId}:{matchId}:{inningsNo}:{snap.LegalBalls / 6}";
            if (!_cache.TryGetValue(key, out _))
            {
                _cache.Set(key, true, TimeSpan.FromMinutes(30));
                var six = ball.Runs == 6;
                await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
                {
                    Type = six ? NotificationTypes.Six : NotificationTypes.Four,
                    Title = six ? "SIX!" : "FOUR!",
                    Body = $"{Name(ball.StrikerId)}, {score}",
                    Preference = p => p.Boundaries,
                    Data = data
                });
            }
        }

        if (ball.Wicket != null)
        {
            var outId = ball.OutPlayerId ?? ball.StrikerId;
            await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
            {
                Type = NotificationTypes.Wicket,
                Title = "WICKET!",
                Body = $"{Name(outId)} {Dismissal(ball, Name)}. {score}",
                Preference = p => p.Wickets,
                Data = data
            });
        }

        if (ball.Runs > 0)
        {
            var totals = await _matches.GetBatterTotalsAsync(ownerId, matchId, inningsNo, ball.StrikerId, seq);
            var before = totals.Runs - ball.Runs;
            int? milestone = before < 100 && totals.Runs >= 100 ? 100 : before < 50 && totals.Runs >= 50 ? 50 : null;
            if (milestone != null)
            {
                await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
                {
                    Type = NotificationTypes.Milestone,
                    Title = milestone == 100 ? "CENTURY!" : "FIFTY!",
                    Body = $"{Name(ball.StrikerId)} reaches {milestone} ({totals.BallsFaced} balls)",
                    Preference = p => p.Milestones,
                    Data = data
                });
            }
        }
    }

    public async Task InningsCompletedAsync(string ownerId, string matchId, int inningsNo)
    {
        var match = await _matches.GetAsync(ownerId, matchId);
        var innings = await _matches.GetInningsAsync(ownerId, matchId, inningsNo);
        if (match == null || innings == null) return;

        var body = ScoreText(match, innings.BattingTeamId, innings.TotalRuns, innings.Wickets, innings.LegalBalls);
        if (inningsNo == 1)
        {
            var chaser = innings.BattingTeamId == match.TeamAId ? match.TeamBShort : match.TeamAShort;
            body = $"Innings break: {Short(match, innings)} {innings.TotalRuns}/{innings.Wickets}. {chaser} need {innings.TotalRuns + 1}";
        }
        else
        {
            body = $"Innings complete: {body}";
        }

        await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
        {
            Type = NotificationTypes.InningsEnd,
            Title = inningsNo == 1 ? "Innings break" : "Innings complete",
            Body = body,
            Preference = p => p.InningsEnd,
            Data = Data(matchId, inningsNo)
        });
    }

    public async Task MatchCompletedAsync(string ownerId, string matchId)
    {
        var m = await _matches.GetAsync(ownerId, matchId);
        if (m == null) return;
        var abandoned = m.Status == "abandoned";
        var fallback = $"{m.TeamAShort} vs {m.TeamBShort} {(abandoned ? "was abandoned" : "has finished")}";
        await SendToAudienceAsync(ownerId, matchId, new OutgoingNotification
        {
            Type = NotificationTypes.MatchResult,
            Title = abandoned ? "Match abandoned" : "Match result",
            Body = string.IsNullOrWhiteSpace(m.ResultText) ? fallback : m.ResultText,
            Preference = p => p.MatchResult,
            Data = Data(matchId, null)
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Owner plus everyone following the match.</summary>
    private async Task SendToAudienceAsync(string ownerId, string matchId, OutgoingNotification template)
    {
        var audience = new HashSet<string> { ownerId };
        foreach (var f in await _matches.GetFollowerIdsAsync(ownerId, matchId)) audience.Add(f);
        foreach (var uid in audience)
        {
            await _notifications.SendToUserAsync(uid, template);
        }
    }

    private static Dictionary<string, string> Data(string matchId, int? inningsNo)
    {
        var d = new Dictionary<string, string> { ["matchId"] = matchId, ["click_action"] = "OPEN_MATCH" };
        if (inningsNo != null) d["inningsNo"] = inningsNo.Value.ToString();
        return d;
    }

    private static string Short(Match m, Innings i) => i.BattingTeamId == m.TeamAId ? m.TeamAShort : m.TeamBShort;

    private static string ScoreText(Match m, string battingTeamId, int runs, int wickets, int legalBalls) =>
        $"{(battingTeamId == m.TeamAId ? m.TeamAShort : m.TeamBShort)} {runs}/{wickets} ({legalBalls / 6}.{legalBalls % 6})";

    private static string Dismissal(Ball b, Func<string?, string> name) => b.Wicket switch
    {
        "bowled" => $"b {name(b.BowlerId)}",
        "caught" => b.FielderId != null ? $"c {name(b.FielderId)} b {name(b.BowlerId)}" : $"c b {name(b.BowlerId)}",
        "lbw" => $"lbw b {name(b.BowlerId)}",
        "stumped" => b.FielderId != null ? $"st {name(b.FielderId)} b {name(b.BowlerId)}" : $"st b {name(b.BowlerId)}",
        "hitWicket" => $"hit wicket b {name(b.BowlerId)}",
        "runOut" => b.FielderId != null ? $"run out ({name(b.FielderId)})" : "run out",
        _ => "out"
    };
}
