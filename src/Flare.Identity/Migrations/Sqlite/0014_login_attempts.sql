-- LoginAttempts: failed-password-attempt throttling for local username/password login
-- (AuthEndpoints.HandleLoginAsync). Keyed by (Username, ClientIp) rather than Username
-- alone - an attacker hammering one account from a single source gets locked out, but a
-- legitimate user typing their own password wrong a few times isn't collaterally locked
-- out by someone else's unrelated attack against the same username from a different IP.
-- No FOREIGN KEY to Users(Id): Username is checked before VerifyPasswordAsync even knows
-- whether the account exists (VerifyPasswordAsync collapses "no such user" and "wrong
-- password" into the same result on purpose - see AuthEndpoints' own remarks), so a row
-- here can legitimately reference a username with no matching account.
CREATE TABLE IF NOT EXISTS LoginAttempts
(
    Username TEXT NOT NULL,
    ClientIp TEXT NOT NULL,
    FailedCount INTEGER NOT NULL,
    LastFailedAt TEXT NOT NULL,
    LockedUntil TEXT NULL,
    PRIMARY KEY (Username, ClientIp)
);
