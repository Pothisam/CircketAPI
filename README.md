# Cricket Scorer API

Backend for the Flutter app **Cricket Scorer** (`com.pothi.cricket`): Firebase-authenticated users, cloud sync of
teams / players / matches, live ball-by-ball scoring, and FCM push notifications.
Implements the "Cricket Scorer: Backend API and Database Specification". ASP.NET Core 7, EF Core 7, SQL Server.

## Solution layout (same layering as CMSAPI)

```
API/Cricket.API                  Controllers, middleware (errors, session check), Program.cs
Libraries/Cricket-Models         DTOs, enums, config models        (no dependencies)
Libraries/Cricket-Repository     EF entities, DbContext, repos     (-> Models)
Libraries/Cricket-Services       Business logic, FCM push, queue   (-> Repository, Models)
Database/CricketDB_Deploy.sql    SQL Server schema (idempotent)
```

## Setup

1. **Create the database**
   `sqlcmd -S <server> -E -i Database\CricketDB_Deploy.sql`
   Creates `CricketDB` (13 tables, 16 indexes). Safe to re-run; it only adds what is missing.
2. **Connection string** – `ConnectionStrings:DefaultConnection` in `appsettings.json` (defaults to LocalDB).
3. **Firebase service account** (needed for push + account deletion; the API starts without it and push is disabled):
   Firebase Console → Project settings → Service accounts → Generate new private key. Then either set
   `FirebaseConfig:CredentialsPath`, or the `GOOGLE_APPLICATION_CREDENTIALS` environment variable.
   Keep the file in a secret manager – `.gitignore` already excludes it.
4. `dotnet run --project API/Cricket.API` → Swagger at `/swagger` (Development).

## Authentication

Every endpoint except `/health`, `/config` and `/public/matches/{shareCode}` needs
`Authorization: Bearer <Firebase ID token>`. Tokens are validated (signature via Google's keys, issuer, audience =
`cricket-run-notification`, expiry). The user id always comes from the token.
A user must call `POST /auth/session` once after sign-in; until then data endpoints return 401.
Admin endpoints require the Firebase custom claim `admin: true`.

## Endpoints (all under `/api/v1`)

| Area | Endpoints |
|---|---|
| Auth | `POST /auth/session`, `POST /auth/logout`, `GET/PATCH/DELETE /me` |
| Devices | `PUT /devices/token`, `GET /devices` (send `X-Device-Id` for `isCurrent`), `PATCH/DELETE /devices/{id}` |
| Teams / Players | `GET /teams`, `GET/PUT/DELETE /teams/{id}`, `GET /teams/{id}/stats`; same for `/players` |
| Matches | `GET /matches` (`?full=true`), `GET/PUT/DELETE /matches/{id}` |
| Live scoring | `POST /matches/{id}/innings`, `PATCH /matches/{id}/innings/{n}`, `POST /matches/{id}/innings/{n}/balls`, `DELETE .../balls/last`, `POST /matches/{id}/complete` |
| Sync | `POST /sync/push`, `GET /sync/pull?since=` |
| Notifications | `GET /notifications`, `POST /notifications/{id}/read`, `POST /notifications/read-all`, `DELETE /notifications/{id}`, `GET/PUT /notifications/preferences`, `POST /notifications/test` |
| Sharing | `POST/DELETE /matches/{id}/share`, `GET /public/matches/{code}`, `POST /matches/follow`, `DELETE /matches/{ownerId}/{id}/follow`, `GET /following` |
| Other | `GET /health`, `GET /config`, `POST /feedback`, `POST /admin/notifications/broadcast` |

Errors use the spec's body `{ "error": { "code", "message", "details" } }`.
A `409` additionally carries `expectedSeq` (ball sequence conflicts) at the top level.

## Behaviour worth knowing

- **Ids are client-supplied** (PUT is an upsert). A body `id` that differs from the URL is rejected.
- **No row versioning.** `PUT` is a plain upsert (last write wins). Tables keep `created_at`, `updated_at` and `deleted_at`
  (sync and soft delete need them); there is no `version` column and no audit trail.
- **Ball endpoint** is idempotent: identical retry → 200, different content at the same `seq` → 409, a gap → 409 `expectedSeq`.
- **Notifications** are sent from a background queue (never blocks scoring). Score text is the score *as of that ball*.
  Boundary pushes are limited to one per over per match. Quiet hours are evaluated in **UTC** (the server doesn't know the user's time zone).
  Only the live → finished transition sends `match_result`, so retries don't double-push.
- **Account deletion** removes every row via DB cascades, then deletes the Firebase user.
- **Rate limits**: 60 req/min per user; 600/min on the ball endpoints.

## Differences from the spec (SQL Server adaptation)

- Text columns are `NVARCHAR`; `JSONB` columns are `NVARCHAR(MAX)` with `ISJSON` checks; times are UTC `DATETIME2(3)`.
- `team_players → players` and `notifications → user_devices` are `NO ACTION` instead of `CASCADE`/`SET NULL`
  (SQL Server rejects multiple cascade paths). The API removes those rows explicitly.
- Added a `feedback` table (the spec lists `POST /feedback` but no table).
- A matching `email`/`phone` already owned by a *different* Firebase uid is not copied onto the new row
  (columns are unique and the primary key is the Firebase uid). Use Firebase account linking to merge identities.

## User profile and player linking (upgrade 001)

Run `Database\CricketDB_Upgrade_001_UserProfile.sql` **after** `CricketDB_Deploy.sql` (safe to re-run). It adds
profile columns to `users` (`gender`, `date_of_birth`, `city`, `is_discoverable`) and `players` (`email`,
`phone_number`, `gender`, `date_of_birth`, `height_cm`, `weight_kg`, `jersey_number`, `city`, `photo_url`,
`linked_user_id`, `is_self`), plus indexes and check constraints, and back-fills a "self" player for existing users.

| Endpoint | Purpose |
|---|---|
| `GET /me/profile`, `PUT /me/profile` | The user's cricket profile (identity + role, batting/bowling style, height, weight, jersey, DOB, gender, city, `isDiscoverable`). `PUT` replaces it and updates the user's own player. |
| `GET /players/search?q=&limit=` | Search my players (name or e-mail, source `mine`) and registered users (name, or **exact** e-mail, source `user`). Min. 2 characters. Other users' e-mails are masked unless searched by the exact address. |
| `POST /players/from-user` `{ "userId" }` | Add a registered user to my player list, linked to that user (only role/styles/city/photo are copied; height, weight, DOB and phone stay private). `201` when created, `200` if already in the list. |
| `PUT /players/{id}` (changed) | The player body now also carries `email`, `phoneNumber`, `gender`, `dateOfBirth`, `heightCm`, `weightKg`, `jerseyNumber`, `city`, `photoUrl`. Output-only: `linkedUserId`, `isSelf`, `isRegistered`. |

How the linking works

- `POST /auth/session` (first sign-in) creates the user **and a player for them in their own list** (`is_self = 1`,
  id `u_<uid>`). If they had already added themselves by hand with their own e-mail, that player is adopted instead.
- E-mail is the match key (case-insensitive). A manual player whose e-mail belongs to a registered user is linked
  to that user (`linked_user_id`) when it is saved. A user who signs up later is linked to every player, in any
  list, that already carries their e-mail.
- Players that are not linked are people who never signed in: they only exist in the list of whoever added them.
- The user's own player cannot be deleted (delete the account instead). `DELETE /me` detaches other lists' links first.
- Users can switch off `isDiscoverable` to stay out of search results.

## Teams and matches I belong to (upgrade 002)

Run `Database\CricketDB_Upgrade_002_Memberships.sql` after upgrade 001 (safe to re-run). It drops the `version` column
from `teams`, `players` and `matches` and adds indexes. No new tables: membership is derived from `team_players` /
`match_players` joined to `players.linked_user_id` (the link between a player in someone's list and a signed-in user).

| Endpoint | Purpose |
|---|---|
| `GET /me/joined-teams` | Teams created by other scorers that contain me |
| `GET /me/joined-teams/{ownerId}/{teamId}` | One of those teams with its squad (public details only: name, role, styles, photo) |
| `GET /me/matches` | My match history in other scorers' matches: I was in the playing XI, or a team I belong to played |
| `GET /me/matches/{ownerId}/{matchId}` | Full scorecard of one of those matches (read only, only for participants) |

Notification `team_added` ("<scorer> added you to <team>") is sent to a registered user when a player linked to them is
added to a team's squad (`PUT /teams/{id}`, also via `POST /sync/push`). Only players that are new in the squad notify.

## Matches between teams of different users (upgrade 003)

Run `Database\CricketDB_Upgrade_003_MatchInvitations.sql` after upgrade 002 (safe to re-run). It adds the table
`match_invitations` and the columns `matches.invitation_id` / `matches.opponent_user_id`.

Flow: the scorer (owner of team A) searches a team of another user, sends an invitation (**pending**), the other owner
**accepts** or **rejects**. An accepted invitation is the scheduled match: both owners see it. The scorer starts it
(toss + both playing XIs); the real match is created in the scorer's account and linked back (**started**).
The opponent's owner and the members of the opponent team see the match through `GET /me/matches`.

| Endpoint | Purpose |
|---|---|
| `GET /teams/search?q=` | **Public** teams of other users by name (min. 2 chars). A team is public or private (`isPublic` on `PUT /teams/{id}`, default private, see upgrade 004); private teams cannot be found or challenged. |
| `POST /match-invitations` | Challenge a team: `{ fromTeamId, toOwnerId, toTeamId, overs, scheduledAt?, venue?, message? }` -> `201`. The other owner is notified (`match_invite`). |
| `GET /match-invitations?box=incoming\|outgoing\|all&status=` | My invitations (`direction` says which side I am) |
| `POST /match-invitations/{id}/accept` / `reject` | Only the owner of the invited team, only while `pending`. The sender is notified (`match_invite_response`). |
| `POST /match-invitations/{id}/cancel` | The sender while `pending`; either side once `accepted`. The other side is notified. |
| `GET /match-invitations/{id}/opponent-squad` | The invited team and its players (with `playerId`), for the scorer to pick the opponent's XI |
| `PUT /matches/{id}` (changed) | A body with `invitationId` links the match to an accepted invitation (teams must be the invited ones). Output: `opponentUserId`. |

Rules: you cannot challenge your own team; one open invitation per pair of teams; only the scorer (owner of the
match row) can score; deleting a user removes the invitations they received and clears `opponent_user_id`.

`GET /players/{id}/teams` (player screen, Teams tab): other scorers' **public** teams in which one of my players (a registered
user) also plays. Empty for players who never signed in, for me, and for users who opted out of being found. Private teams
are never listed. The teams of my own list and their matches come from the data already on the device.

## Captain, handing a team over, team limit (upgrade 005)

Run `Database\CricketDB_Upgrade_005_TeamRolesAndManagers.sql` after upgrade 004 (safe to re-run). It adds
`teams.captain_player_id` / `vice_captain_player_id` and the table `team_managers`.

**Captain / vice-captain** belong to the team: `PUT /teams/{id}` takes and returns `captainPlayerId` and
`viceCaptainPlayerId`. Both must be in the squad; if a player leaves the squad the role is cleared. The squad order
is the order of `playerIds`.

**Handing a team over** (the creator stays the owner):

| Endpoint | Who | Purpose |
|---|---|---|
| `GET /teams/{id}/managers` | owner | Users the team was handed to |
| `POST /teams/{id}/managers` `{ userId }` | owner | Hand the team over (max. 5 managers). The user is notified (`team_manager`). |
| `DELETE /teams/{id}/managers/{userId}` | owner | Take it back |
| `GET /me/managed-teams` | manager | Teams handed over to me |
| `GET /me/managed-teams/{ownerId}/{teamId}` | manager | Team + squad + the owner's other players |
| `PUT /me/managed-teams/{ownerId}/{teamId}` | manager | Name, short name, colour, squad order, captain, vice-captain |
| `POST /me/managed-teams/{ownerId}/{teamId}/players` | manager | Add a registered user (`userId`) or a new player (`name`) |
| `DELETE /me/managed-teams/{ownerId}/{teamId}` | manager | Give the team back |

**Admins are chosen from the squad.** In the app the owner opens the menu of a player in the team and chooses **Make admin**
(`POST /teams/{id}/managers`). Only a registered player (one with an app account) who is in the squad can be made admin
(`400` otherwise). If that player leaves the squad, the admin access is removed. A manager cannot remove themselves from the
squad (they give the team back instead).

A manager can run the team but **cannot** delete it, change its visibility, hand it to others, or remove the
creator from the squad (`403`). Only the owner does those. Players a manager adds are created in the owner's list.

**Team limit** (enforced by `PUT /teams/{id}` and `POST /sync/push`, error code `LIMIT_REACHED`, HTTP 403):
a user owns at most **3 teams: 1 public and 2 private**, duplicates included. Only creating a team or changing its
visibility is checked; teams that already exist are kept. Teams handed to a manager do not count for the manager.

**Player limit.** A user's player list holds up to **50 players** (their own player and registered users added from search count too).
`PUT /players/{id}`, `POST /players/from-user`, `POST /sync/push` and an admin adding a player to a team (the owner's list) return
`LIMIT_REACHED` (403) for a NEW player once the list is full. Existing players are never touched.

`DELETE /me/joined-teams/{ownerId}/{teamId}`: leave a team somebody else added me to. The players of the creator's list that are linked to me
are taken out of the squad (their captain / vice-captain role and any admin access go with them), the creator is notified (`team_left`)
and the team disappears from `GET /me/joined-teams`. The creator can add the player again. `GET /me/joined-teams/{ownerId}/{teamId}` now also
returns `isCaptain`, `isViceCaptain` and `isOwner` for each squad member.
