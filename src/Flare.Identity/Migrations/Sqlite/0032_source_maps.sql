-- Uploaded JavaScript source maps for symbolicating browser stack traces on /errors
-- (ADR-0152). One row per (service, version, bundle); Content is the gzipped map. Version is
-- the service.version (or vcs revision) the bundle was built for, so an old release's errors
-- keep resolving after a newer one is uploaded.
CREATE TABLE IF NOT EXISTS SourceMaps
(
    ServiceName TEXT NOT NULL,
    Version TEXT NOT NULL,
    Bundle TEXT NOT NULL,
    Content BLOB NOT NULL,
    SizeBytes INTEGER NOT NULL,
    UploadedAt TEXT NOT NULL,
    PRIMARY KEY (ServiceName, Version, Bundle)
);
