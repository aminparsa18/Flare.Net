# How to speed up filters on a frequently used log attribute

If you filter logs by the same attribute all the time (for example
`http.route`, `tenant.id` or `k8s.namespace.name`), promote that key to its own
column. Flare then reads a single column with its own skip index, not the whole
attribute map on every row. Search, charts and alert rules that filter on the
key all get faster. Results don't change.

## Prerequisites

- An **Admin** account (or authentication turned off). Other roles can see the
  list of promoted attributes but can't change it.
- The exact attribute key and which set it's in: **Log** (log record
  attributes), **Resource** (for example `service.namespace`,
  `k8s.namespace.name`) or **Scope**.

## Promote a key

1. Open **Indexing** and scroll to **Promoted attributes**.
2. Pick the set (**Log**, **Resource** or **Scope**) and type the key, for
   example `http.route`.
3. Leave **Backfill existing data** on unless your `logs` table is very large
   and its older data expires soon. See [Backfill](#backfill).
4. Click **Promote**.

The key appears in the table with its column name, for example
`attr_log_http_route`. Filters on the key use the new column right away on the
Flare.Api instance you used. Other Flare.Api instances and the alert worker
pick it up within 30 seconds.

Keys may contain letters, digits and `. _ - : / @`, up to 200 characters. You
can promote up to 50 keys.

## Backfill

With **Backfill existing data** on, ClickHouse rewrites existing data in the
background so older rows also get the column and the skip index. The status
column shows **Backfilling** until that finishes, then **Active**. On a large
table this can take a while and uses disk I/O.

Filters on the key return correct results during a backfill, and also when you
turn backfill off. Only the speed-up on older data is missing. Without a
backfill, older data gets the column only as ClickHouse merges it in the
background.

## Which filters get faster

The column replaces the attribute-map lookup for:

- **equals**
- **not equals**, **in** and **not in**, unless one of the compared values is
  empty

**exists**, **absent** and the regex operators still read the attribute map,
because the column can't tell a missing key from an empty value.

## Demote a key

Click **Demote** on its row and confirm. Flare drops the column and its skip
index, and filters on the key go back to reading the attribute map. No log
data is lost: the attribute is still stored in the map.

## Related

- [ADR-0062: Promoted attribute columns](../../docs-internal/adr/0062-promoted-attribute-columns.md)
  covers the design: naming, cluster-mode DDL, and why the table schema itself
  is the registry.
