// Stand-in for SvelteKit's `$app/env/public` under Node (see resolve-hooks.mjs). The benchmarks
// never make a request, so every variable is simply unset.
export const PUBLIC_API_URL: string | undefined = undefined;
export const PUBLIC_GRAFANA_TRACE_URL: string | undefined = undefined;
