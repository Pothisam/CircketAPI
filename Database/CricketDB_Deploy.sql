/* =====================================================================
   Cricket Scorer - SQL Server deployment script
   Target : SQL Server 2017+ (uses filtered indexes, ISJSON, DATETIME2)
   Safe to re-run: every object is created only if it does not exist.

   Usage  : sqlcmd -S <server> -E -i CricketDB_Deploy.sql
            (or open in SSMS and run). Edit @DbName below if needed.

   Notes on adapting the PostgreSQL spec to SQL Server
   - TIMESTAMPTZ -> DATETIME2(3), always UTC (the API writes UTC).
   - JSONB       -> NVARCHAR(MAX) + CHECK (ISJSON(...) = 1).
   - BOOLEAN     -> BIT, BIGSERIAL -> BIGINT IDENTITY, TIME -> TIME(0).
   - Text columns are NVARCHAR (player/team names may be non-Latin).
   - Unique columns that may be NULL (email, phone, share_code) use
     FILTERED unique indexes, because SQL Server allows only one NULL
     in a plain UNIQUE constraint.
   - SQL Server rejects "multiple cascade paths". team_players ->
     players and notifications -> user_devices are therefore NO ACTION;
     the API removes those rows explicitly. Deleting a user still works
     (all rows go through the ON DELETE CASCADE chain from users).
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- required for filtered indexes; sqlcmd defaults to OFF
SET ANSI_NULLS ON;
GO

DECLARE @DbName sysname = N'CricketDB';
IF DB_ID(@DbName) IS NULL
BEGIN
    DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DbName) + N';';
    EXEC (@sql);
    PRINT 'Database ' + @DbName + ' created.';
END
GO

USE [CricketDB];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------- users */
IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.users (
        id              NVARCHAR(64)  NOT NULL,              -- Firebase UID
        email           NVARCHAR(255) NULL,
        phone_number    NVARCHAR(20)  NULL,                  -- E.164
        display_name    NVARCHAR(120) NULL,
        photo_url       NVARCHAR(MAX) NULL,
        auth_provider   NVARCHAR(20)  NOT NULL,              -- google.com | phone
        email_verified  BIT           NOT NULL CONSTRAINT df_users_email_verified DEFAULT 0,
        is_active       BIT           NOT NULL CONSTRAINT df_users_is_active DEFAULT 1,
        last_login_at   DATETIME2(3)  NULL,
        created_at      DATETIME2(3)  NOT NULL CONSTRAINT df_users_created DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2(3)  NOT NULL CONSTRAINT df_users_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
END
GO

/* -------------------------------------------------------- user_devices */
IF OBJECT_ID(N'dbo.user_devices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_devices (
        id                      BIGINT IDENTITY(1,1) NOT NULL,
        user_id                 NVARCHAR(64)  NOT NULL,
        fcm_token               NVARCHAR(450) NOT NULL,
        device_id               NVARCHAR(128) NULL,
        platform                NVARCHAR(10)  NOT NULL,
        device_model            NVARCHAR(120) NULL,
        os_version              NVARCHAR(40)  NULL,
        app_version             NVARCHAR(20)  NULL,
        locale                  NVARCHAR(10)  NULL,
        notifications_enabled   BIT NOT NULL CONSTRAINT df_dev_notif DEFAULT 1,
        is_active               BIT NOT NULL CONSTRAINT df_dev_active DEFAULT 1,
        last_seen_at            DATETIME2(3) NOT NULL CONSTRAINT df_dev_seen DEFAULT SYSUTCDATETIME(),
        created_at              DATETIME2(3) NOT NULL CONSTRAINT df_dev_created DEFAULT SYSUTCDATETIME(),
        updated_at              DATETIME2(3) NOT NULL CONSTRAINT df_dev_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_user_devices PRIMARY KEY (id),
        CONSTRAINT fk_devices_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
        CONSTRAINT ck_devices_platform CHECK (platform IN ('android','ios','web'))
    );
END
GO

/* --------------------------------------------------------------- teams */
IF OBJECT_ID(N'dbo.teams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.teams (
        id          NVARCHAR(64) NOT NULL,
        user_id     NVARCHAR(64) NOT NULL,
        name        NVARCHAR(80) NOT NULL,
        short_name  NVARCHAR(8)  NOT NULL,
        color_index INT          NOT NULL CONSTRAINT df_teams_color DEFAULT 0,
        version     INT          NOT NULL CONSTRAINT df_teams_version DEFAULT 1,
        created_at  DATETIME2(3) NOT NULL CONSTRAINT df_teams_created DEFAULT SYSUTCDATETIME(),
        updated_at  DATETIME2(3) NOT NULL CONSTRAINT df_teams_updated DEFAULT SYSUTCDATETIME(),
        deleted_at  DATETIME2(3) NULL,
        CONSTRAINT pk_teams PRIMARY KEY (user_id, id),
        CONSTRAINT fk_teams_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE
    );
END
GO

/* ------------------------------------------------------------- players */
IF OBJECT_ID(N'dbo.players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.players (
        id            NVARCHAR(64)  NOT NULL,
        user_id       NVARCHAR(64)  NOT NULL,
        name          NVARCHAR(120) NOT NULL,
        role          NVARCHAR(20)  NOT NULL CONSTRAINT df_players_role DEFAULT 'batter',
        batting_style NVARCHAR(30)  NOT NULL CONSTRAINT df_players_bat DEFAULT 'Right-hand bat',
        bowling_style NVARCHAR(30)  NOT NULL CONSTRAINT df_players_bowl DEFAULT 'None',
        version       INT           NOT NULL CONSTRAINT df_players_version DEFAULT 1,
        created_at    DATETIME2(3)  NOT NULL CONSTRAINT df_players_created DEFAULT SYSUTCDATETIME(),
        updated_at    DATETIME2(3)  NOT NULL CONSTRAINT df_players_updated DEFAULT SYSUTCDATETIME(),
        deleted_at    DATETIME2(3)  NULL,
        CONSTRAINT pk_players PRIMARY KEY (user_id, id),
        CONSTRAINT fk_players_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
        CONSTRAINT ck_players_role CHECK (role IN ('batter','bowler','allRounder','wicketKeeper')),
        CONSTRAINT ck_players_bat  CHECK (batting_style IN ('Right-hand bat','Left-hand bat')),
        CONSTRAINT ck_players_bowl CHECK (bowling_style IN ('None','Right-arm fast','Right-arm medium','Right-arm off spin',
                                          'Right-arm leg spin','Left-arm fast','Left-arm medium','Left-arm orthodox','Left-arm chinaman'))
    );
END
GO

/* -------------------------------------------------------- team_players */
IF OBJECT_ID(N'dbo.team_players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_players (
        user_id    NVARCHAR(64) NOT NULL,
        team_id    NVARCHAR(64) NOT NULL,
        player_id  NVARCHAR(64) NOT NULL,
        sort_order INT          NOT NULL CONSTRAINT df_tp_sort DEFAULT 0,
        CONSTRAINT pk_team_players PRIMARY KEY (user_id, team_id, player_id),
        CONSTRAINT fk_tp_team   FOREIGN KEY (user_id, team_id)   REFERENCES dbo.teams(user_id, id)   ON DELETE CASCADE,
        -- NO ACTION (not CASCADE): a second cascade path from users would be rejected by SQL Server.
        -- The API deletes a player's team_players rows explicitly.
        CONSTRAINT fk_tp_player  FOREIGN KEY (user_id, player_id) REFERENCES dbo.players(user_id, id)
    );
END
GO

/* ------------------------------------------------------------- matches */
IF OBJECT_ID(N'dbo.matches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.matches (
        id               NVARCHAR(64)  NOT NULL,
        user_id          NVARCHAR(64)  NOT NULL,
        team_a_id        NVARCHAR(64)  NOT NULL,
        team_b_id        NVARCHAR(64)  NOT NULL,
        team_a_name      NVARCHAR(80)  NOT NULL,
        team_b_name      NVARCHAR(80)  NOT NULL,
        team_a_short     NVARCHAR(8)   NOT NULL,
        team_b_short     NVARCHAR(8)   NOT NULL,
        overs            INT           NOT NULL,
        toss_winner_id   NVARCHAR(64)  NOT NULL,
        elected_to_bat   BIT           NOT NULL,
        match_date       DATETIME2(3)  NOT NULL,
        venue            NVARCHAR(200) NOT NULL CONSTRAINT df_matches_venue DEFAULT '',
        status           NVARCHAR(12)  NOT NULL CONSTRAINT df_matches_status DEFAULT 'live',
        winner_id        NVARCHAR(64)  NULL,
        result_text      NVARCHAR(255) NOT NULL CONSTRAINT df_matches_result DEFAULT '',
        visibility       NVARCHAR(10)  NOT NULL CONSTRAINT df_matches_vis DEFAULT 'private',
        share_code       NVARCHAR(16)  NULL,
        version          INT           NOT NULL CONSTRAINT df_matches_version DEFAULT 1,
        created_at       DATETIME2(3)  NOT NULL CONSTRAINT df_matches_created DEFAULT SYSUTCDATETIME(),
        updated_at       DATETIME2(3)  NOT NULL CONSTRAINT df_matches_updated DEFAULT SYSUTCDATETIME(),
        deleted_at       DATETIME2(3)  NULL,
        CONSTRAINT pk_matches PRIMARY KEY (user_id, id),
        CONSTRAINT fk_matches_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
        CONSTRAINT ck_matches_status CHECK (status IN ('live','completed','abandoned')),
        CONSTRAINT ck_matches_vis    CHECK (visibility IN ('private','link','public')),
        CONSTRAINT ck_matches_overs  CHECK (overs BETWEEN 1 AND 100)
    );
END
GO

/* ------------------------------------------------------- match_players */
IF OBJECT_ID(N'dbo.match_players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_players (
        user_id       NVARCHAR(64)  NOT NULL,
        match_id      NVARCHAR(64)  NOT NULL,
        player_id     NVARCHAR(64)  NOT NULL,
        team_id       NVARCHAR(64)  NOT NULL,
        player_name   NVARCHAR(120) NOT NULL,   -- snapshot
        batting_order INT           NULL,
        CONSTRAINT pk_match_players PRIMARY KEY (user_id, match_id, player_id),
        CONSTRAINT fk_mp_match FOREIGN KEY (user_id, match_id) REFERENCES dbo.matches(user_id, id) ON DELETE CASCADE
    );
END
GO

/* ------------------------------------------------------------- innings */
IF OBJECT_ID(N'dbo.innings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.innings (
        user_id          NVARCHAR(64) NOT NULL,
        match_id         NVARCHAR(64) NOT NULL,
        innings_no       INT          NOT NULL,
        batting_team_id  NVARCHAR(64) NOT NULL,
        bowling_team_id  NVARCHAR(64) NOT NULL,
        striker_id       NVARCHAR(64) NULL,
        non_striker_id   NVARCHAR(64) NULL,
        bowler_id        NVARCHAR(64) NULL,
        completed        BIT          NOT NULL CONSTRAINT df_innings_completed DEFAULT 0,
        total_runs       INT          NOT NULL CONSTRAINT df_innings_runs DEFAULT 0,
        wickets          INT          NOT NULL CONSTRAINT df_innings_wkts DEFAULT 0,
        legal_balls      INT          NOT NULL CONSTRAINT df_innings_legal DEFAULT 0,
        CONSTRAINT pk_innings PRIMARY KEY (user_id, match_id, innings_no),
        CONSTRAINT fk_innings_match FOREIGN KEY (user_id, match_id) REFERENCES dbo.matches(user_id, id) ON DELETE CASCADE,
        CONSTRAINT ck_innings_no CHECK (innings_no BETWEEN 1 AND 4)
    );
END
GO

/* --------------------------------------------------------------- balls */
IF OBJECT_ID(N'dbo.balls', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.balls (
        user_id         NVARCHAR(64)  NOT NULL,
        match_id        NVARCHAR(64)  NOT NULL,
        innings_no      INT           NOT NULL,
        seq             INT           NOT NULL,        -- 0-based ordering key
        striker_id      NVARCHAR(64)  NOT NULL,
        non_striker_id  NVARCHAR(64)  NOT NULL,
        bowler_id       NVARCHAR(64)  NOT NULL,
        runs            INT           NOT NULL CONSTRAINT df_balls_runs DEFAULT 0,
        extra           NVARCHAR(10)  NOT NULL CONSTRAINT df_balls_extra DEFAULT 'none',
        extra_runs      INT           NOT NULL CONSTRAINT df_balls_extra_runs DEFAULT 0,
        wicket          NVARCHAR(12)  NULL,
        out_player_id   NVARCHAR(64)  NULL,
        fielder_id      NVARCHAR(64)  NULL,
        shot_direction  NVARCHAR(12)  NULL,
        shot_path       NVARCHAR(MAX) NULL,            -- JSON [[x,y],...]
        loft            NVARCHAR(8)   NULL,
        created_at      DATETIME2(3)  NOT NULL CONSTRAINT df_balls_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_balls PRIMARY KEY (user_id, match_id, innings_no, seq),
        CONSTRAINT fk_balls_innings FOREIGN KEY (user_id, match_id, innings_no)
            REFERENCES dbo.innings(user_id, match_id, innings_no) ON DELETE CASCADE,
        CONSTRAINT ck_balls_runs       CHECK (runs BETWEEN 0 AND 7),
        CONSTRAINT ck_balls_extra_runs CHECK (extra_runs BETWEEN 0 AND 10),
        CONSTRAINT ck_balls_extra      CHECK (extra IN ('none','wide','noBall','bye','legBye')),
        CONSTRAINT ck_balls_wicket     CHECK (wicket IS NULL OR wicket IN ('bowled','caught','lbw','runOut','stumped','hitWicket')),
        CONSTRAINT ck_balls_direction  CHECK (shot_direction IS NULL OR shot_direction IN
            ('straight','midOff','cover','point','thirdMan','fineLeg','squareLeg','midWicket','midOn')),
        CONSTRAINT ck_balls_loft       CHECK (loft IS NULL OR loft IN ('ground','lofted','aerial')),
        CONSTRAINT ck_balls_shot_path  CHECK (shot_path IS NULL OR ISJSON(shot_path) = 1)
    );
END
GO

/* ------------------------------------------------------- notifications */
IF OBJECT_ID(N'dbo.notifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.notifications (
        id              BIGINT IDENTITY(1,1) NOT NULL,
        user_id         NVARCHAR(64)  NOT NULL,
        type            NVARCHAR(30)  NOT NULL,
        title           NVARCHAR(160) NOT NULL,
        body            NVARCHAR(500) NOT NULL,
        data            NVARCHAR(MAX) NULL,           -- JSON object of strings
        device_id       BIGINT        NULL,           -- NULL = all devices
        fcm_message_id  NVARCHAR(200) NULL,
        status          NVARCHAR(12)  NOT NULL CONSTRAINT df_notif_status DEFAULT 'queued',
        error           NVARCHAR(255) NULL,
        read_at         DATETIME2(3)  NULL,
        created_at      DATETIME2(3)  NOT NULL CONSTRAINT df_notif_created DEFAULT SYSUTCDATETIME(),
        sent_at         DATETIME2(3)  NULL,
        CONSTRAINT pk_notifications PRIMARY KEY (id),
        CONSTRAINT fk_notif_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
        -- NO ACTION: SET NULL here would be a second cascade path from users. The API clears
        -- notifications.device_id before it deletes a device.
        CONSTRAINT fk_notif_device FOREIGN KEY (device_id) REFERENCES dbo.user_devices(id),
        CONSTRAINT ck_notif_status CHECK (status IN ('queued','sent','failed')),
        CONSTRAINT ck_notif_data   CHECK (data IS NULL OR ISJSON(data) = 1)
    );
END
GO

/* ------------------------------------------- notification_preferences */
IF OBJECT_ID(N'dbo.notification_preferences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_preferences (
        user_id           NVARCHAR(64) NOT NULL,
        login_alerts      BIT NOT NULL CONSTRAINT df_np_login DEFAULT 1,
        match_start       BIT NOT NULL CONSTRAINT df_np_start DEFAULT 1,
        boundaries        BIT NOT NULL CONSTRAINT df_np_bound DEFAULT 1,
        wickets           BIT NOT NULL CONSTRAINT df_np_wkts DEFAULT 1,
        milestones        BIT NOT NULL CONSTRAINT df_np_miles DEFAULT 1,
        innings_end       BIT NOT NULL CONSTRAINT df_np_inn DEFAULT 1,
        match_result      BIT NOT NULL CONSTRAINT df_np_result DEFAULT 1,
        quiet_hours_start TIME(0) NULL,
        quiet_hours_end   TIME(0) NULL,
        updated_at        DATETIME2(3) NOT NULL CONSTRAINT df_np_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_notification_preferences PRIMARY KEY (user_id),
        CONSTRAINT fk_np_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE
    );
END
GO

/* ----------------------------------------------------- match_followers */
IF OBJECT_ID(N'dbo.match_followers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_followers (
        match_owner_id NVARCHAR(64) NOT NULL,
        match_id       NVARCHAR(64) NOT NULL,
        follower_id    NVARCHAR(64) NOT NULL,
        created_at     DATETIME2(3) NOT NULL CONSTRAINT df_mf_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_match_followers PRIMARY KEY (match_owner_id, match_id, follower_id),
        CONSTRAINT fk_mf_follower FOREIGN KEY (follower_id) REFERENCES dbo.users(id) ON DELETE CASCADE
    );
END
GO

/* ------------------------------------------------------------ feedback
   Not in the original spec; backs POST /feedback.                       */
IF OBJECT_ID(N'dbo.feedback', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.feedback (
        id          BIGINT IDENTITY(1,1) NOT NULL,
        user_id     NVARCHAR(64)  NOT NULL,
        message     NVARCHAR(2000) NOT NULL,
        app_version NVARCHAR(20)  NULL,
        created_at  DATETIME2(3)  NOT NULL CONSTRAINT df_feedback_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_feedback PRIMARY KEY (id),
        CONSTRAINT fk_feedback_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE
    );
END
GO

/* -------------------------------------------------------------- indexes
   Each index is guarded on its own, so a re-run after a partial failure
   still creates anything that is missing.                               */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_users_phone' AND object_id = OBJECT_ID(N'dbo.users'))
    CREATE UNIQUE INDEX ux_users_phone ON dbo.users(phone_number) WHERE phone_number IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_devices_token' AND object_id = OBJECT_ID(N'dbo.user_devices'))
    CREATE UNIQUE INDEX ux_devices_token ON dbo.user_devices(fcm_token);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_devices_user' AND object_id = OBJECT_ID(N'dbo.user_devices'))
    CREATE INDEX idx_devices_user ON dbo.user_devices(user_id) WHERE is_active = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_devices_user_device' AND object_id = OBJECT_ID(N'dbo.user_devices'))
    CREATE INDEX idx_devices_user_device ON dbo.user_devices(user_id, device_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_teams_sync' AND object_id = OBJECT_ID(N'dbo.teams'))
    CREATE INDEX idx_teams_sync ON dbo.teams(user_id, updated_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_players_sync' AND object_id = OBJECT_ID(N'dbo.players'))
    CREATE INDEX idx_players_sync ON dbo.players(user_id, updated_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_tp_player' AND object_id = OBJECT_ID(N'dbo.team_players'))
    CREATE INDEX idx_tp_player ON dbo.team_players(user_id, player_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matches_status' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE INDEX idx_matches_status ON dbo.matches(user_id, status, match_date DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_matches_share_code' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE UNIQUE INDEX ux_matches_share_code ON dbo.matches(share_code) WHERE share_code IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_balls_striker' AND object_id = OBJECT_ID(N'dbo.balls'))
    CREATE INDEX idx_balls_striker ON dbo.balls(user_id, striker_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_notif_user' AND object_id = OBJECT_ID(N'dbo.notifications'))
    CREATE INDEX idx_notif_user ON dbo.notifications(user_id, created_at DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_notif_unread' AND object_id = OBJECT_ID(N'dbo.notifications'))
    CREATE INDEX idx_notif_unread ON dbo.notifications(user_id) WHERE read_at IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_mf_follower' AND object_id = OBJECT_ID(N'dbo.match_followers'))
    CREATE INDEX idx_mf_follower ON dbo.match_followers(follower_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_users_email' AND object_id = OBJECT_ID(N'dbo.users'))
    CREATE UNIQUE INDEX ux_users_email ON dbo.users(email) WHERE email IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matches_sync' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE INDEX idx_matches_sync ON dbo.matches(user_id, updated_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_balls_bowler' AND object_id = OBJECT_ID(N'dbo.balls'))
    CREATE INDEX idx_balls_bowler ON dbo.balls(user_id, bowler_id);
GO

/* ---------------------------------------------------------------------
   Least-privilege login for the API (optional - uncomment and set a
   strong password, or use your own provisioning).

   CREATE LOGIN cricket_api WITH PASSWORD = '<strong-password>';
   CREATE USER  cricket_api FOR LOGIN cricket_api;
   ALTER ROLE db_datareader ADD MEMBER cricket_api;
   ALTER ROLE db_datawriter ADD MEMBER cricket_api;
   --------------------------------------------------------------------- */

PRINT 'Cricket database schema is up to date.';
GO
