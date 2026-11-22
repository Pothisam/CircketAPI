/* =====================================================================
   Cricket Scorer - upgrade 003: match invitations (matches between teams
   that belong to different users)

   Run AFTER CricketDB_Upgrade_002_Memberships.sql. Safe to re-run.

   Flow
   1. The scorer (owner of team A) searches for a team of another user and
      sends an invitation                          -> status 'pending'
   2. The owner of team B accepts or rejects        -> 'accepted' / 'rejected'
      (the scorer can cancel a pending invitation; either side can cancel an
      accepted fixture before it starts)            -> 'cancelled'
   3. An accepted invitation is the scheduled match: both owners see it.
   4. The scorer starts it (toss + playing XI). The real match row is created
      in the scorer's account and linked back here    -> 'started'
      matches.opponent_user_id lets the opponent's owner (and the members of
      the opponent team) see the live match and its scorecard.

   No version / audit columns (created_at, updated_at only, as elsewhere).
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [CricketDB];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ------------------------------------------------------ match_invitations */
IF OBJECT_ID(N'dbo.match_invitations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_invitations (
        id               NVARCHAR(64)  NOT NULL,
        from_user_id     NVARCHAR(64)  NOT NULL,            -- the scorer: owner of team A, sends the invitation
        from_team_id     NVARCHAR(64)  NOT NULL,
        from_team_name   NVARCHAR(80)  NOT NULL,            -- snapshots, so the invitation stays readable
        from_team_short  NVARCHAR(8)   NOT NULL,
        to_user_id       NVARCHAR(64)  NOT NULL,            -- owner of team B, accepts or rejects
        to_team_id       NVARCHAR(64)  NOT NULL,
        to_team_name     NVARCHAR(80)  NOT NULL,
        to_team_short    NVARCHAR(8)   NOT NULL,
        overs            INT           NOT NULL,
        scheduled_at     DATETIME2(3)  NULL,
        venue            NVARCHAR(200) NOT NULL CONSTRAINT df_matchinv_venue DEFAULT '',
        message          NVARCHAR(300) NULL,
        status           NVARCHAR(12)  NOT NULL CONSTRAINT df_matchinv_status DEFAULT 'pending',
        match_id         NVARCHAR(64)  NULL,                -- set when the scorer starts the match
        created_at       DATETIME2(3)  NOT NULL CONSTRAINT df_matchinv_created DEFAULT SYSUTCDATETIME(),
        updated_at       DATETIME2(3)  NOT NULL CONSTRAINT df_matchinv_updated DEFAULT SYSUTCDATETIME(),
        responded_at     DATETIME2(3)  NULL,
        CONSTRAINT pk_match_invitations PRIMARY KEY (id),
        CONSTRAINT fk_matchinv_from FOREIGN KEY (from_user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
        -- NO ACTION: a second cascade path from users is not allowed. The API removes these rows
        -- before it deletes a user.
        CONSTRAINT fk_matchinv_to   FOREIGN KEY (to_user_id)   REFERENCES dbo.users(id),
        CONSTRAINT ck_matchinv_status CHECK (status IN ('pending','accepted','rejected','cancelled','started')),
        CONSTRAINT ck_matchinv_overs  CHECK (overs BETWEEN 1 AND 100),
        CONSTRAINT ck_matchinv_users  CHECK (from_user_id <> to_user_id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matchinv_to' AND object_id = OBJECT_ID(N'dbo.match_invitations'))
    CREATE INDEX idx_matchinv_to ON dbo.match_invitations(to_user_id, status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matchinv_from' AND object_id = OBJECT_ID(N'dbo.match_invitations'))
    CREATE INDEX idx_matchinv_from ON dbo.match_invitations(from_user_id, status);
-- Only one open invitation between the same two teams.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_matchinv_pending' AND object_id = OBJECT_ID(N'dbo.match_invitations'))
    CREATE UNIQUE INDEX ux_matchinv_pending
        ON dbo.match_invitations(from_user_id, from_team_id, to_user_id, to_team_id) WHERE status = 'pending';
GO

/* --------------------------------------------------------------- matches */
IF COL_LENGTH('dbo.matches', 'invitation_id') IS NULL
    ALTER TABLE dbo.matches ADD invitation_id NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.matches', 'opponent_user_id') IS NULL
    ALTER TABLE dbo.matches ADD opponent_user_id NVARCHAR(64) NULL;     -- owner of team B in a cross-user match
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matches_opponent' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE INDEX idx_matches_opponent ON dbo.matches(opponent_user_id) WHERE opponent_user_id IS NOT NULL;
-- Search teams of other users by name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_teams_name' AND object_id = OBJECT_ID(N'dbo.teams'))
    CREATE INDEX idx_teams_name ON dbo.teams(name) WHERE deleted_at IS NULL;
GO

PRINT 'Upgrade 003 (match invitations) applied.';
GO
