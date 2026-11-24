/* =====================================================================
   Cricket Scorer - upgrade 005: captain / vice-captain, team managers,
   team limit

   Run AFTER CricketDB_Upgrade_004_TeamVisibility.sql. Safe to re-run.

   1. teams.captain_player_id / vice_captain_player_id
      The captain and the vice-captain belong to the TEAM (the same person can lead
      one team and be an ordinary player in another). Both must be in the squad; the
      API clears them when the player leaves the squad. No foreign key: the squad
      is validated by the API.

   2. team_managers  (hand a team over to other users)
      The creator (teams.user_id) stays the owner. A manager sees the team on
      their own device and can run it: change name / colour, add, remove and
      re-order players, set captain and vice-captain. A manager cannot delete the
      team, change its visibility, appoint managers, or remove the creator from
      the team. Only the owner can do those.

   3. Team limit (enforced by the API, not by the database)
      A user can own at most 3 teams: 1 public and 2 private (duplicates count).
      Teams that were already created before this rule are kept.
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

IF COL_LENGTH('dbo.teams', 'captain_player_id') IS NULL
    ALTER TABLE dbo.teams ADD captain_player_id NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.teams', 'vice_captain_player_id') IS NULL
    ALTER TABLE dbo.teams ADD vice_captain_player_id NVARCHAR(64) NULL;
GO

IF OBJECT_ID(N'dbo.team_managers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_managers (
        owner_user_id    NVARCHAR(64) NOT NULL,                -- creator of the team
        team_id          NVARCHAR(64) NOT NULL,
        manager_user_id  NVARCHAR(64) NOT NULL,                -- the user the team was handed to
        created_at       DATETIME2(3) NOT NULL CONSTRAINT df_team_managers_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_team_managers PRIMARY KEY (owner_user_id, team_id, manager_user_id),
        CONSTRAINT fk_team_managers_team FOREIGN KEY (owner_user_id, team_id)
            REFERENCES dbo.teams(user_id, id) ON DELETE CASCADE,
        -- NO ACTION: a second cascade path from users is not allowed. The API removes these rows
        -- before it deletes a user.
        CONSTRAINT fk_team_managers_user FOREIGN KEY (manager_user_id) REFERENCES dbo.users(id),
        CONSTRAINT ck_team_managers_distinct CHECK (owner_user_id <> manager_user_id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_team_managers_manager' AND object_id = OBJECT_ID(N'dbo.team_managers'))
    CREATE INDEX idx_team_managers_manager ON dbo.team_managers(manager_user_id);
GO

PRINT 'Upgrade 005 (captain, team managers) applied.';
GO
