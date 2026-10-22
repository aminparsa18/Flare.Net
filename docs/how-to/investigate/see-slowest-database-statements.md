# How to see your slowest database statements

The **Database** tab ranks the database statements your services run, so you can see which ones cost the most time, run most often, or fail most. Flare builds it from the database spans it already stores. You don't need to change your instrumentation.

## How statements are grouped

- A span is a database span when it carries `db.system.name` (or the older `db.system`).
- The statement is the query text (`db.query.text` or `db.statement`) with string and number literals replaced by `?`, so `WHERE id = 42` and `WHERE id = 7` count as one statement. This is the same text the N+1 tab shows.
- When a span has no query text, Flare uses the operation and table instead, for example `GET sessions`.
- Rows are keyed by service, database system and statement. Both attribute generations (`db.system.name` and `db.system`) land in the same row.
- Spans with no parent count too, unlike N+1 detection.

## Read the table

1. Open **Traces**, then the **Database** tab.
2. Pick a time window. Optionally enter a service name or a database system such as `postgresql`.
3. Choose how to rank: **Slowest overall** (total time), **Highest p95**, **Most calls** or **Most errors**. The table lists up to 100 statements.
4. **Calls/min** is the call count divided by the length of the window. **Avg ms** and **p95 ms** are per execution. **Total ms** is the sum over every execution.
5. Click **Example trace** to open the slowest execution of that statement in its trace.

If your services use trace sampling, calls, errors and total time are scaled by each span's sample weight, so rates are not understated.

## Which tab to use

- **Database** answers "which statement costs the most across everything".
- **N+1** answers "which statement does one request repeat many times".
- The duration comparison on a span's detail answers "is this one execution slow for its kind".

The list is computed from your spans when you open it. The API behind it is `POST /api/traces/db-statements`.
