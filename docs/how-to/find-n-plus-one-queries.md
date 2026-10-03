# How to find N+1 queries

An N+1 query is a database statement that one piece of code runs over and over inside a single request, typically once per row of an earlier result. Flare finds them from the database spans it already stores. You don't need to change your instrumentation.

## How a pattern is detected

Flare groups the database spans of one trace by parent span and statement. A group of **10 or more** identical statements under one parent is an N+1 pattern.

- A span is a database span when it carries `db.system.name` (or the older `db.system`). Database spans with no parent are ignored.
- The statement is the query text (`db.query.text` or `db.statement`) with string and number literals replaced by `?`.
- When a span has no query text, Flare uses the operation and table instead, for example `SELECT users`.

## See it in one trace

1. Open a trace from **Traces**.
2. In the **Waterfall** tab, a parent span that ran an N+1 pattern shows a badge such as `N+1: 48× SELECT * FROM orders WHERE customer_id = ?`.

## Find the worst offenders

1. Open **Traces**, then the **N+1** tab.
2. Pick a time window. Optionally enter a service name, or change the minimum number of repeats (default 10, range 2 to 1000).
3. The table lists up to 50 statements, worst first by total time. **Traces** is how many traces showed the pattern. **Max repeats** is the highest repeat count under a single parent.
4. Click **Example trace** to open the worst case and find the badge.

The list is computed from your spans when you open it, so it also covers data stored before you first opened the page. The API behind it is `POST /api/traces/n-plus-one`.
