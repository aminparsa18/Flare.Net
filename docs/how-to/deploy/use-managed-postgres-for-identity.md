# How to use a managed PostgreSQL for the identity store

Move Flare's identity store to a PostgreSQL server you already run. The
identity store holds users, sessions, ingest API keys, personal access tokens
and auth settings. The server can be Amazon RDS or Aurora PostgreSQL, Azure
Database for PostgreSQL flexible server, Neon or Supabase. By default the
identity store is an SQLite file. PostgreSQL lets `ingest` and `api` run on
different hosts. For why there are two providers, see
[ADR-0111](../../../docs-internal/adr/0111-pluggable-identity-store-postgres.md).
For the configuration keys, see
[the authentication reference](../../reference/authentication-config.md#configuration-reference).

Switching provider starts with an **empty** identity database. Flare does not
copy users, keys or settings from an existing SQLite file. The first person to
open the dashboard creates a new admin, or you can set `Identity:Admin:*` to
create one at startup.

## Prerequisites

- PostgreSQL 11 or later. Flare's own deployments use 17.
- An empty database, plus a role that owns it. `ingest` and `api` both create
  and upgrade the tables when they start, so the role needs DDL rights on the
  schema, not only read/write. Flare cannot run with one role for migrations
  and a weaker one for queries.
- Password authentication. Flare does not get tokens for Microsoft Entra ID or
  AWS IAM database authentication.
- Network access from the `ingest` and `api` containers to the server.
  `alert-worker` does not connect to the identity store.

Flare needs PL/pgSQL triggers and advisory locks. Databases that only speak
the PostgreSQL wire protocol, such as Aurora DSQL, CockroachDB and YugabyteDB,
are not supported.

## Steps

### 1. Create the database and role

Connect as your server's admin user and run:

```sql
CREATE ROLE flare LOGIN PASSWORD 'change-me';
CREATE DATABASE flare_identity OWNER flare;
```

On PostgreSQL 15 and later, only the database owner can create tables in the
`public` schema. The `OWNER flare` clause covers this.

On **Neon**, create the role and database in the console instead. On
**Supabase** you get one database per project, so do step 3 instead of
creating a database.

### 2. Write the connection string

Flare passes `Identity:ConnectionString` to Npgsql unchanged. It must be in
Npgsql's `Key=Value;` format. URIs such as `postgresql://user:pw@host/db`,
which Neon and Supabase show by default, are rejected. Convert the URI like
this:

| URI part | Npgsql key |
|---|---|
| `user` | `Username` |
| `pw` | `Password` (percent-decode it first) |
| `host`, `:port` | `Host`, `Port` |
| `/db` | `Database` |
| `?sslmode=require` | `SslMode=Require` |

Choose `SslMode` for your security needs. `Require` encrypts the connection
but does not check the server's certificate. `VerifyFull` checks the
certificate and the host name. Examples for each service:

**Amazon RDS / Aurora PostgreSQL**

```text
Host=mydb.abc123.eu-west-1.rds.amazonaws.com;Port=5432;Database=flare_identity;Username=flare;Password=...;SslMode=VerifyFull;Root Certificate=/etc/flare/certs/rds-global-bundle.pem
```

RDS certificates are signed by Amazon's own CA, which is not in the
container's trust store. For `VerifyFull`, download
`https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem` and mount
it into both containers (step 4). Otherwise use `SslMode=Require`.

**Azure Database for PostgreSQL flexible server**

```text
Host=myserver.postgres.database.azure.com;Port=5432;Database=flare_identity;Username=flare;Password=...;SslMode=VerifyFull
```

Azure's certificates chain to public roots that the Flare images already
trust, so no certificate file is needed.

**Neon**

```text
Host=ep-cool-name-123456-pooler.eu-central-1.aws.neon.tech;Database=flare_identity;Username=flare;Password=...;SslMode=VerifyFull;Timeout=30
```

`Timeout=30` gives a compute that scaled to zero time to wake up. Without it,
the first connection can hit Npgsql's 15-second default.

**Supabase**

```text
Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.yourprojectref;Password=...;SslMode=Require;Search Path=flare
```

Supabase signs its database certificates with its own CA. For `VerifyFull`,
download the CA from the project's database settings, mount it like the RDS
bundle, and add `Root Certificate=...`. Use the pooler host. The direct `db.<project>.supabase.co` host is IPv6-only
unless you buy the IPv4 add-on. `Search Path=flare` is explained in step 3.

**Connection poolers.** Flare's locks are transaction-scoped, and Flare does
not use prepared statements or per-session settings. It should therefore work
behind a transaction-mode pooler (Supabase port 6543, PgBouncer, Neon's
`-pooler` hosts). If you see errors that only appear through the pooler,
switch to session mode or a direct connection.

### 3. Supabase only: keep the tables out of `public`

Supabase's Data API serves the `public` schema. Projects created before
mid-2026 also grant the `anon` and `authenticated` roles access to every new
table there. Flare's tables include password hashes, session-token hashes and
API-key hashes. Put them in a schema of their own:

```sql
CREATE SCHEMA flare;
```

Then add `Search Path=flare` to the connection string, as in the example
above. Flare does not create the schema itself, so run this first. Don't add
`flare` to the Data API's exposed schemas.

### 4. Point Flare at the database

`ingest` and `api` must use the **same** connection string. Pick the section
for your install.

**Docker Compose.** Create `docker-compose.override.yml` next to
`docker-compose.yml`. Compose loads it automatically:

```yaml
services:
  ingest:
    environment:
      Identity__Provider: "Postgres"
      Identity__ConnectionString: "${FLARE_IDENTITY_CONNECTION_STRING}"
    # Only for SslMode=VerifyFull with a private CA, such as RDS:
    volumes:
      - ./certs:/etc/flare/certs:ro
  api:
    environment:
      Identity__Provider: "Postgres"
      Identity__ConnectionString: "${FLARE_IDENTITY_CONNECTION_STRING}"
    volumes:
      - ./certs:/etc/flare/certs:ro
```

Put `FLARE_IDENTITY_CONNECTION_STRING=...` in `.env` and run
`docker compose up -d`. Flare ignores `Identity__DbPath` once the provider is
`Postgres`.

**Helm.** Store the connection string in a Secret and turn off the bundled
Postgres:

```sh
kubectl create secret generic flare-identity \
  --from-literal=identity-connection-string='Host=...;SslMode=VerifyFull'
```

```yaml
identity:
  provider: Postgres
  postgres:
    enabled: false
    external:
      existingSecret: flare-identity
```

The chart has no option for extra volumes yet, so you can't mount the RDS CA
bundle. Use `SslMode=Require` for RDS and Supabase on Helm. Azure and Neon
work with `VerifyFull` because their roots are already trusted. See
[Deploy with Helm](deploy-with-helm.md#choose-the-identity-store).

**Aspire (`CommunityToolkit.Aspire.Hosting.Flare`).** This needs a toolkit
version that has `WithIdentityDatabase`. Declare the database as a connection
string and pass it in:

```csharp
var identity = builder.AddConnectionString("flare-identity");

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithIdentityDatabase(identity);
```

Set the value with
`dotnet user-secrets set "ConnectionStrings:flare-identity" "Host=..."` in the
AppHost project. If Aspire provisions the server with
`AddAzurePostgresFlexibleServer`, call `.WithPasswordAuthentication(...)` on
it. That resource uses Entra ID by default, and Flare cannot get Entra tokens.

## Verify it worked

On first start, `ingest` or `api` logs one line per migration:

```text
Applying identity migration 0001_baseline.sql
Applying identity migration 0002_password_set_tokens.sql
...
```

Whichever process starts second waits for the first to finish and applies
nothing. Then check the database:

```sql
SELECT name FROM schema_migrations ORDER BY name;
```

Each file in `src/Flare.Identity/Migrations/Postgres/` should have a row.
Open the dashboard. It should offer to create the first admin account, or you
can sign in with the `Identity:Admin:*` account if you set one.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Format of the initialization string does not conform to specification` | The connection string is a URI. Convert it (step 2). |
| `Identity:Provider is 'Postgres' but Identity:ConnectionString is not set.` | Set `Identity__ConnectionString` on that container, on both `ingest` and `api`. |
| `no pg_hba.conf entry ... no encryption` or `SSL connection is required` | Add `SslMode=Require` or `SslMode=VerifyFull`. |
| `The remote certificate is invalid` | You set `VerifyFull` and the server uses a private CA (RDS, Supabase). Mount the CA bundle and set `Root Certificate`, or use `Require`. |
| `42501: permission denied for schema public` | The role does not own the database. Run `ALTER DATABASE flare_identity OWNER TO flare;`, or `GRANT CREATE ON SCHEMA public TO flare;`. |
| `3F000: no schema has been selected to create in` | `Search Path` names a schema that does not exist. Run `CREATE SCHEMA` (step 3). |
| `No password has been provided but the backend requires one` | The connection string has no password. This usually means an Aspire Azure resource using Entra ID. Call `WithPasswordAuthentication`. |
| The first start times out on Neon | Add `Timeout=30` so the compute has time to wake up. |
| Login works on one host but not another | `ingest` and `api` point at different databases. Use the same connection string on both. |
