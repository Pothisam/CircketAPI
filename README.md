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
A `409` additionally carries `current` (version conflicts) or `expectedSeq` (ball sequence conflicts) at the top level.

## Behaviour worth knowing

- **Ids are client-supplied** (PUT is an upsert). A body `id` that differs from the URL is rejected.
- **Version conflicts** (teams, players, matches): `PUT` with a `version` lower than the stored one → 409. Omit `version` for an unconditional write.
  Ball/innings/complete calls update `updated_at` (so sync sees them) but do not bump the match `version`.
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
