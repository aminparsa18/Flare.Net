-- Synthetic DNS/UDP/ICMP probes, migration 0060.
--
-- See `docs-internal/adr/0134-synthetic-dns-udp-icmp-probes.md`. Additive: empty means "any answer", so existing
-- monitors are unaffected. Existing numbered migrations are immutable; run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS ExpectedAnswer String DEFAULT '' CODEC(ZSTD(1));
