/* =====================================================================
   Cricket Scorer - upgrade 004: public / private teams

   Run AFTER CricketDB_Upgrade_003_MatchInvitations.sql. Safe to re-run.

   teams.is_public
     1 = public : other users can find the team by name (GET /teams/search)
                  and challenge it to a match
     0 = private: only the owner sees it (default). Players who were added to the
                  team still see the teams they belong to.

   Existing teams become PRIVATE: owners switch a team to public in the app
   (Teams -> edit team). To make every existing team public instead, run:
       UPDATE dbo.teams SET is_public = 1 WHERE deleted_at IS NULL;
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

IF COL_LENGTH('dbo.teams', 'is_public') IS NULL
    ALTER TABLE dbo.teams ADD is_public BIT NOT NULL CONSTRAINT df_teams_is_public DEFAULT 0;
GO

-- Search only looks at public teams.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_teams_public_name' AND object_id = OBJECT_ID(N'dbo.teams'))
    CREATE INDEX idx_teams_public_name ON dbo.teams(name) WHERE is_public = 1 AND deleted_at IS NULL;
GO

PRINT 'Upgrade 004 (team visibility) applied.';
GO
