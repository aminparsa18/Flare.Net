-- Optional read-only access token for fetching source from the repo host (inline source on
-- /errors, docs-internal/adr/0096-inline-exception-source.md). Write-only through the API, like
-- LdapSettings.BindPassword: NULL = none.
ALTER TABLE SourceLinks ADD COLUMN AccessToken TEXT;
