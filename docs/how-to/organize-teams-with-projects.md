# How to organize teams with projects

By default every signed-in user sees every service, dashboard, alert and ingest key. A **project** gives one team a boundary: a named set of services plus the people who may see them. Projects need authentication to be on (see [Configure authentication](configure-authentication.md)).

## Create a project

1. Sign in as a global Admin and open **Settings > Projects**, then **New project**.
2. Name it and list the services it owns, one `service.name` per line. A trailing `*` matches a prefix (`checkout-*`). A bare `*` is rejected so a project cannot silently own everything.
3. Open **Members** and add users with a project role: **Admin**, **Member** or **Viewer**.

Overlapping patterns across projects are allowed; a user sees the union of their projects.

## What a project scopes

Once at least one project exists, a non-admin user sees only the services their projects allow, across logs, traces, metrics, errors, dashboards panels, SLOs and live tail. A service that matches no project is visible to global Admins only. A non-admin who belongs to no project sees nothing. Global Admins always see everything. Narrowing a project's patterns takes effect immediately.

Not scoped: infrastructure pages that are not keyed by service (Hosts, Kubernetes, ingestion health) and broker-level backlog gauges (Kafka lag, queue depth).

## Assign dashboards, alerts, SLOs, views and ingest keys

The create forms for dashboards, alert rules, SLOs and saved views have a **Project** picker, and so does **New key** under **Settings > Ingest keys**. An object with a project is visible only to that project's members and global Admins. An object with no project is instance-wide, as before. To move an ingest key later, use the folder button in its row.

Writing to a project-owned object needs the project role Admin or Member. The project role can only narrow your global role: a global Viewer cannot edit even as a project Member. A project Admin may change any dashboard in the project.

Managing ingest keys stays global-Admin-only, because a key can ingest under any `service.name`.

## Switch project

Once you belong to a project, a project switcher appears in the top bar. Choosing a project filters dashboards, alerts, SLOs and saved views to it (instance-wide ones always show) and makes it the default for new objects. It does not change which telemetry you can query; that always follows your memberships.

Deleting a project hides its objects from everyone but global Admins until one reassigns them.
