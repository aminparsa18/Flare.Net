-- Re-key error-issue state for normalized exception messages, migration 0087 (docs-internal/adr/0258-error-message-normalization.md).
--
-- Groups are now (exception.type, NORMALIZED exception.message) - GUIDs, numbers and quoted values replaced by
-- placeholders (`Flare.Api.Errors.ErrorMessageNormalizer`). An `error_issues` row is keyed by a hash of that pair, so
-- a row saved under a raw message that carries such a value would never match its group again. This rewrites those
-- rows: for each live row whose message changes under normalization, insert a new version under the new Id (the same
-- SHA-256 prefix `ErrorIssueFingerprint` computes) and tombstone the old Id. When several old rows collapse into one
-- group the most recently updated wins. Rows whose message is unchanged are untouched, so a group without a variable
-- part keeps its state. The expression below must stay identical to `ErrorMessageNormalizer.Sql`; a unit test checks it.
--
-- Safe to re-run: after it succeeds no live row has a message that normalization would change.
--
-- Existing numbered migrations are immutable once merged, hence a new file. Like migrations 0002-0086, run this by
-- hand via `clickhouse-client` against any already-running instance.

INSERT INTO clickhousedb.error_issues
    (Id, ExceptionType, ExceptionMessage, IsDeleted, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt, IssueUrl)
SELECT
    lower(hex(substring(SHA256(concat(ExceptionType, char(31), NormMessage)), 1, 16))),
    ExceptionType, NormMessage, 0, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, now64(3), IssueUrl
FROM (
    SELECT *, replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(ExceptionMessage, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<guid>'), '\\b0[xX][0-9a-fA-F]+\\b', '<hex>'), '\\b[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]+)?(Z|[+-][0-9]{2}:?[0-9]{2})?', '<ts>'), '(^|[\\s(\\[{=:,])\'[^\'\\n]*\'', '\\1\'<s>\''), '(^|[\\s(\\[{=:,])"[^"\\n]*"', '\\1"<s>"'), '\\b[0-9]+\\b', '<n>') AS NormMessage
    FROM (SELECT * FROM clickhousedb.error_issues ORDER BY UpdatedAt DESC LIMIT 1 BY Id)
    WHERE IsDeleted = 0
)
WHERE NormMessage != ExceptionMessage
ORDER BY UpdatedAt DESC
LIMIT 1 BY ExceptionType, NormMessage;

INSERT INTO clickhousedb.error_issues
    (Id, ExceptionType, ExceptionMessage, IsDeleted, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt, IssueUrl)
SELECT
    Id, ExceptionType, ExceptionMessage, 1, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, now64(3), IssueUrl
FROM (
    SELECT *, replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(replaceRegexpAll(ExceptionMessage, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<guid>'), '\\b0[xX][0-9a-fA-F]+\\b', '<hex>'), '\\b[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]+)?(Z|[+-][0-9]{2}:?[0-9]{2})?', '<ts>'), '(^|[\\s(\\[{=:,])\'[^\'\\n]*\'', '\\1\'<s>\''), '(^|[\\s(\\[{=:,])"[^"\\n]*"', '\\1"<s>"'), '\\b[0-9]+\\b', '<n>') AS NormMessage
    FROM (SELECT * FROM clickhousedb.error_issues ORDER BY UpdatedAt DESC LIMIT 1 BY Id)
    WHERE IsDeleted = 0
)
WHERE NormMessage != ExceptionMessage;
