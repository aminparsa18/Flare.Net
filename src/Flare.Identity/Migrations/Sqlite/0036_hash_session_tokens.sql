-- Session tokens are now stored as SHA-256 digests (ADR-0209). Existing rows hold raw tokens that no longer match
-- any lookup, so drop them rather than leave live credentials in the table; users sign in again once.
DELETE FROM Sessions;
