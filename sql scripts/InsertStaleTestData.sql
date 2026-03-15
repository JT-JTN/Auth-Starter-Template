-- ============================================================
-- Seed stale refresh tokens to test ExpiredTokenCleanupJob
-- Table name: UserTokens  (mapped via RefreshTokenConfiguration)
-- ============================================================

DECLARE @UserId NVARCHAR(450);
SELECT TOP 1 @UserId = Id FROM AspNetUsers ORDER BY (SELECT NULL);

IF @UserId IS NULL
BEGIN
    RAISERROR('No users found. Register at least one account first.', 16, 1);
    RETURN;
END

PRINT 'Inserting test tokens for UserId: ' + @UserId;

-- ── Category 1: Naturally expired AND past 30-day retention window ────────────
INSERT INTO UserTokens (Id, Token, ExpiresUtc, CreatedAtUtc, RevokedAtUtc, UserId, IpAddress, UserAgent)
VALUES
    (NEWID(), 'test-expired-1', DATEADD(day, -35, GETUTCDATE()), DATEADD(day, -42, GETUTCDATE()), NULL, @UserId, '127.0.0.1', 'TestAgent/1.0'),
    (NEWID(), 'test-expired-2', DATEADD(day, -40, GETUTCDATE()), DATEADD(day, -47, GETUTCDATE()), NULL, @UserId, '127.0.0.1', 'TestAgent/1.0'),
    (NEWID(), 'test-expired-3', DATEADD(day, -60, GETUTCDATE()), DATEADD(day, -67, GETUTCDATE()), NULL, @UserId, '127.0.0.1', 'TestAgent/1.0'),
    (NEWID(), 'test-expired-4', DATEADD(day, -90, GETUTCDATE()), DATEADD(day, -97, GETUTCDATE()), NULL, @UserId, '127.0.0.1', 'TestAgent/1.0'),
    (NEWID(), 'test-expired-5', DATEADD(day, -365, GETUTCDATE()), DATEADD(day, -372, GETUTCDATE()), NULL, @UserId, '127.0.0.1', 'TestAgent/1.0');

-- ── Category 2: Explicitly revoked AND past 30-day retention window ───────────
INSERT INTO UserTokens (Id, Token, ExpiresUtc, CreatedAtUtc, RevokedAtUtc, UserId, IpAddress, UserAgent)
VALUES
    (NEWID(), 'test-revoked-1', DATEADD(day, 7, GETUTCDATE()), DATEADD(day, -35, GETUTCDATE()), DATEADD(day, -35, GETUTCDATE()), @UserId, '192.168.1.1', 'TestAgent/2.0'),
    (NEWID(), 'test-revoked-2', DATEADD(day, 7, GETUTCDATE()), DATEADD(day, -40, GETUTCDATE()), DATEADD(day, -40, GETUTCDATE()), @UserId, '192.168.1.1', 'TestAgent/2.0'),
    (NEWID(), 'test-revoked-3', DATEADD(day, 7, GETUTCDATE()), DATEADD(day, -50, GETUTCDATE()), DATEADD(day, -50, GETUTCDATE()), @UserId, '192.168.1.1', 'TestAgent/2.0'),
    (NEWID(), 'test-revoked-4', DATEADD(day, 7, GETUTCDATE()), DATEADD(day, -60, GETUTCDATE()), DATEADD(day, -60, GETUTCDATE()), @UserId, '192.168.1.1', 'TestAgent/2.0'),
    (NEWID(), 'test-revoked-5', DATEADD(day, 7, GETUTCDATE()), DATEADD(day, -90, GETUTCDATE()), DATEADD(day, -90, GETUTCDATE()), @UserId, '192.168.1.1', 'TestAgent/2.0');

-- ── Category 3: Should NOT be deleted (within 30-day retention window) ────────
INSERT INTO UserTokens (Id, Token, ExpiresUtc, CreatedAtUtc, RevokedAtUtc, UserId, IpAddress, UserAgent)
VALUES
    (NEWID(), 'test-recent-1', DATEADD(day, -10, GETUTCDATE()), DATEADD(day, -17, GETUTCDATE()), NULL, @UserId, '10.0.0.1', 'TestAgent/3.0'),
    (NEWID(), 'test-recent-2', DATEADD(day, -5,  GETUTCDATE()), DATEADD(day, -12, GETUTCDATE()), NULL, @UserId, '10.0.0.1', 'TestAgent/3.0');

-- ── Verify counts before running the job ──────────────────────────────────────
SELECT 'Total test tokens' AS Info, COUNT(*) AS Count FROM UserTokens WHERE Token LIKE 'test-%';

SELECT 'Will be deleted (stale)' AS Info, COUNT(*) AS Count
FROM UserTokens WHERE Token LIKE 'test-%'
  AND (ExpiresUtc < DATEADD(day,-30,GETUTCDATE())
       OR (RevokedAtUtc IS NOT NULL AND RevokedAtUtc < DATEADD(day,-30,GETUTCDATE())));

SELECT 'Will survive (within retention)' AS Info, COUNT(*) AS Count
FROM UserTokens WHERE Token LIKE 'test-%'
  AND NOT (ExpiresUtc < DATEADD(day,-30,GETUTCDATE())
           OR (RevokedAtUtc IS NOT NULL AND RevokedAtUtc < DATEADD(day,-30,GETUTCDATE())));