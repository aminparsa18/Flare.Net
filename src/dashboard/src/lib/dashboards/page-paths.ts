// Path helper for the dashboards feature - mirrors `$lib/saved-views/page-paths.ts`'s
// role, much narrower: a dashboard has exactly one route shape (there's no per-page-type
// base path to switch on, since a dashboard isn't scoped to one Explorer page - see
// docs-internal/adr/0023-custom-dashboards.md).

export function dashboardPath(dashboard: { id: string }): string {
	return `/dashboards/${dashboard.id}`;
}
