/* =====================================================================
   Cricket Scorer - upgrade 001: user profile + player <-> user linking

   Run AFTER CricketDB_Deploy.sql. Safe to re-run (every change is guarded).

   What it does
   1. users    : gender, date_of_birth, city, is_discoverable
   2. players  : email, phone_number, gender, date_of_birth, height_cm,
                 weight_kg, jersey_number, city, photo_url,
                 linked_user_id (the signed-in user this player IS),
                 is_self        (the player that represents the owner)
   3. Constraints + indexes for email matching and search
   4. Back-fills a "self" player for every user that already exists

   Rules the API enforces on top of this schema
   - Every user gets exactly one is_self player in their own roster (created
     by POST /auth/session on first sign-in). Its id is  'u_' + user id.
   - players.email is matched (case-insensitive) with users.email. A manual
     player whose email belongs to a registered user is linked to that user
     (linked_user_id); a user who signs up later is linked to every player,
     in any roster, that already carries their email.
   - linked_user_id is NO ACTION (not SET NULL) because a second cascade path
     from users would be rejected by SQL Server. The API clears the links
     before it deletes a user.
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

/* ---------------------------------------------------------------- users */
IF COL_LENGTH('dbo.users', 'gender') IS NULL
    ALTER TABLE dbo.users ADD gender NVARCHAR(10) NULL;
IF COL_LENGTH('dbo.users', 'date_of_birth') IS NULL
    ALTER TABLE dbo.users ADD date_of_birth DATE NULL;
IF COL_LENGTH('dbo.users', 'city') IS NULL
    ALTER TABLE dbo.users ADD city NVARCHAR(80) NULL;
IF COL_LENGTH('dbo.users', 'is_discoverable') IS NULL
    ALTER TABLE dbo.users ADD is_discoverable BIT NOT NULL
        CONSTRAINT df_users_discoverable DEFAULT 1;
GO

/* -------------------------------------------------------------- players */
IF COL_LENGTH('dbo.players', 'email') IS NULL
    ALTER TABLE dbo.players ADD email NVARCHAR(255) NULL;
IF COL_LENGTH('dbo.players', 'phone_number') IS NULL
    ALTER TABLE dbo.players ADD phone_number NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.players', 'gender') IS NULL
    ALTER TABLE dbo.players ADD gender NVARCHAR(10) NULL;
IF COL_LENGTH('dbo.players', 'date_of_birth') IS NULL
    ALTER TABLE dbo.players ADD date_of_birth DATE NULL;
IF COL_LENGTH('dbo.players', 'height_cm') IS NULL
    ALTER TABLE dbo.players ADD height_cm DECIMAL(5,1) NULL;
IF COL_LENGTH('dbo.players', 'weight_kg') IS NULL
    ALTER TABLE dbo.players ADD weight_kg DECIMAL(5,1) NULL;
IF COL_LENGTH('dbo.players', 'jersey_number') IS NULL
    ALTER TABLE dbo.players ADD jersey_number INT NULL;
IF COL_LENGTH('dbo.players', 'city') IS NULL
    ALTER TABLE dbo.players ADD city NVARCHAR(80) NULL;
IF COL_LENGTH('dbo.players', 'photo_url') IS NULL
    ALTER TABLE dbo.players ADD photo_url NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.players', 'linked_user_id') IS NULL
    ALTER TABLE dbo.players ADD linked_user_id NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.players', 'is_self') IS NULL
    ALTER TABLE dbo.players ADD is_self BIT NOT NULL CONSTRAINT df_players_is_self DEFAULT 0;
GO

/* --------------------------------------------------------- constraints */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_players_linked_user')
    ALTER TABLE dbo.players ADD CONSTRAINT fk_players_linked_user
        FOREIGN KEY (linked_user_id) REFERENCES dbo.users(id);          -- NO ACTION, see header

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_players_gender')
    ALTER TABLE dbo.players ADD CONSTRAINT ck_players_gender
        CHECK (gender IS NULL OR gender IN ('male','female','other'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_players_height')
    ALTER TABLE dbo.players ADD CONSTRAINT ck_players_height
        CHECK (height_cm IS NULL OR height_cm BETWEEN 50 AND 260);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_players_weight')
    ALTER TABLE dbo.players ADD CONSTRAINT ck_players_weight
        CHECK (weight_kg IS NULL OR weight_kg BETWEEN 10 AND 300);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_players_jersey')
    ALTER TABLE dbo.players ADD CONSTRAINT ck_players_jersey
        CHECK (jersey_number IS NULL OR jersey_number BETWEEN 0 AND 999);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_users_gender')
    ALTER TABLE dbo.users ADD CONSTRAINT ck_users_gender
        CHECK (gender IS NULL OR gender IN ('male','female','other'));
GO

/* ------------------------------------------------------------- indexes */
-- Match a player to a user by email (case-insensitive collation is the default).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_players_email' AND object_id = OBJECT_ID(N'dbo.players'))
    CREATE INDEX idx_players_email ON dbo.players(email) WHERE email IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_players_linked_user' AND object_id = OBJECT_ID(N'dbo.players'))
    CREATE INDEX idx_players_linked_user ON dbo.players(linked_user_id) WHERE linked_user_id IS NOT NULL;
-- One "self" player per user.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_players_self' AND object_id = OBJECT_ID(N'dbo.players'))
    CREATE UNIQUE INDEX ux_players_self ON dbo.players(user_id) WHERE is_self = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_players_name' AND object_id = OBJECT_ID(N'dbo.players'))
    CREATE INDEX idx_players_name ON dbo.players(user_id, name);
-- Search registered users by name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_users_display_name' AND object_id = OBJECT_ID(N'dbo.users'))
    CREATE INDEX idx_users_display_name ON dbo.users(display_name) WHERE display_name IS NOT NULL;
GO

/* ------------------------------------------------------------ back-fill */
-- 1. A "self" player for every existing user that has none.
INSERT INTO dbo.players (id, user_id, name, email, photo_url, linked_user_id, is_self)
SELECT LEFT(N'u_' + u.id, 64),
       u.id,
       LEFT(COALESCE(NULLIF(LTRIM(RTRIM(u.display_name)), N''),
                     CASE WHEN CHARINDEX(N'@', u.email) > 1 THEN LEFT(u.email, CHARINDEX(N'@', u.email) - 1) END,
                     N'Me'), 120),
       u.email,
       u.photo_url,
       u.id,
       1
FROM dbo.users u
WHERE NOT EXISTS (SELECT 1 FROM dbo.players p WHERE p.user_id = u.id AND p.is_self = 1)
  AND NOT EXISTS (SELECT 1 FROM dbo.players p WHERE p.user_id = u.id AND p.id = LEFT(N'u_' + u.id, 64));

-- 2. Link existing manual players whose email belongs to a registered user.
UPDATE p
SET    p.linked_user_id = u.id
FROM   dbo.players p
JOIN   dbo.users u ON u.email = p.email
WHERE  p.linked_user_id IS NULL AND p.email IS NOT NULL AND p.is_self = 0;
GO

PRINT 'Upgrade 001 (user profile) applied.';
GO
