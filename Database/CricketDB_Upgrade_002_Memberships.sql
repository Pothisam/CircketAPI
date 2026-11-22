/* =====================================================================
   Cricket Scorer - upgrade 002: team / match membership + no versioning

   Run AFTER CricketDB_Upgrade_001_UserProfile.sql. Safe to re-run.

   1. Removes the `version` column from teams, players and matches.
      There is no row versioning / audit trail at table level for now
      (created_at / updated_at / deleted_at stay: sync and soft-delete use them).
   2. Adds indexes that make "the teams I was added to" and "the matches I
      played in" fast. No new tables are needed: membership is derived from
      team_players / match_players joined to players.linked_user_id
      (the link created by upgrade 001).
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

/* ------------------------------------------------- drop `version` columns */
DECLARE @tables TABLE (name sysname);
INSERT INTO @tables VALUES (N'teams'), (N'players'), (N'matches');

DECLARE @t sysname, @sql nvarchar(max), @df sysname;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @tables;
OPEN c;
FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF COL_LENGTH(N'dbo.' + @t, 'version') IS NOT NULL
    BEGIN
        SET @df = NULL;
        SELECT @df = dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns col ON col.object_id = dc.parent_object_id AND col.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.' + @t) AND col.name = 'version';

        IF @df IS NOT NULL
        BEGIN
            SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' DROP CONSTRAINT ' + QUOTENAME(@df) + N';';
            EXEC (@sql);
        END
        SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' DROP COLUMN version;';
        EXEC (@sql);
        PRINT 'Dropped version from dbo.' + @t;
    END
    FETCH NEXT FROM c INTO @t;
END
CLOSE c;
DEALLOCATE c;
GO

/* ------------------------------------------------------------- indexes */
-- "Which teams am I in?"  players.linked_user_id -> team_players -> teams
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_team_players_player' AND object_id = OBJECT_ID(N'dbo.team_players'))
    CREATE INDEX idx_team_players_player ON dbo.team_players(user_id, player_id) INCLUDE (team_id);

-- "Which matches did I play in?"  players.linked_user_id -> match_players -> matches
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_match_players_player' AND object_id = OBJECT_ID(N'dbo.match_players'))
    CREATE INDEX idx_match_players_player ON dbo.match_players(user_id, player_id) INCLUDE (match_id);

-- Matches of a team that I belong to.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matches_team_a' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE INDEX idx_matches_team_a ON dbo.matches(user_id, team_a_id) WHERE deleted_at IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_matches_team_b' AND object_id = OBJECT_ID(N'dbo.matches'))
    CREATE INDEX idx_matches_team_b ON dbo.matches(user_id, team_b_id) WHERE deleted_at IS NULL;
GO

PRINT 'Upgrade 002 (memberships, no versioning) applied.';
GO
