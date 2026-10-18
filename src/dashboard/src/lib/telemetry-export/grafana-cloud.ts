// Grafana Cloud preset for a forwarding target (ADR-0155/0157): the OTLP gateway URL and the Basic auth
// header are derived from the three values the Grafana Cloud "OpenTelemetry" tile shows, so nobody
// hand-builds `https://otlp-gateway-<zone>.grafana.net/otlp` or base64-encodes `instance:token`.

/**
 * The gateway zone (`prod-us-central-0`) from what a user may paste: the zone, the region without the `prod-` prefix,
 * or the full gateway URL. Null when it is none of those.
 */
export function normalizeGrafanaZone(input: string): string | null {
	let zone = input.trim().toLowerCase();
	const fromUrl = /^(?:https?:\/\/)?otlp-gateway-([a-z0-9-]+)\.grafana\.net(?:\/.*)?$/.exec(zone);
	if (fromUrl) zone = fromUrl[1];
	if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(zone)) return null;
	return zone.startsWith('prod-') ? zone : `prod-${zone}`;
}

export function grafanaCloudEndpoint(zone: string): string {
	return `https://otlp-gateway-${zone}.grafana.net/otlp`;
}

/** The Basic credential Grafana Cloud's OTLP gateway expects: the stack's numeric instance id and an access policy token. */
export function grafanaCloudHeaders(instanceId: string, token: string): Record<string, string> {
	return { Authorization: `Basic ${btoa(`${instanceId.trim()}:${token.trim()}`)}` };
}

/** True for the numeric stack id Grafana Cloud shows next to the OTLP endpoint. */
export function isGrafanaInstanceId(value: string): boolean {
	return /^\d+$/.test(value.trim());
}
