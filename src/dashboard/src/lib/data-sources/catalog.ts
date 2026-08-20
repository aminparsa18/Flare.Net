// Content for /data-sources (the "how do I send logs to Flare" guide, reached from the
// Logs empty state - see routes/data-sources/+page.svelte). Deliberately not a 1:1 port
// of OpenObserve's page: OpenObserve accepts an arbitrary per-vendor bulk-JSON API, so it
// can list 40+ individual integrations. Flare.Ingest only speaks OTLP (gRPC :4317, HTTP
// :4318 POST /v1/logs - see src/Flare.Ingest/README.md), so the honest equivalent for
// "Databases"/"Web Servers"/etc. is "run the OpenTelemetry Collector with that source's
// receiver, export OTLP to Flare" rather than a bespoke snippet per product. This is the
// curated "focused core" set: platforms apps commonly run on, plus the languages/SDKs
// most likely to be emitting the logs in the first place.
//
// Ingest auth: IngestApiKeyValidationMiddleware (src/Flare.Ingest/Auth/) only enforces a
// Bearer token when IngestAuthOptions.IngestKeyRequired is turned on - off by default, so
// every snippet below is written for the common anonymous-ingest case. The "custom" item's
// last step covers the opt-in key case without building key-management UI here.

import type { Component } from 'svelte';
import BoxesIcon from '@lucide/svelte/icons/boxes';
import ContainerIcon from '@lucide/svelte/icons/container';
import TerminalIcon from '@lucide/svelte/icons/terminal';
import MonitorIcon from '@lucide/svelte/icons/monitor';
import WebhookIcon from '@lucide/svelte/icons/webhook';
// One icon per language/framework instead of a shared placeholder - infinity and hexagon
// are the actual .NET/Node.js logo shapes (not just puns), coffee/zap are the
// well-known Java/Go associations. @lucide/svelte ships generic icons only (no brand
// logos), so this is the closest a pure lucide set gets to "looks like the real thing".
import InfinityIcon from '@lucide/svelte/icons/infinity';
import CodeIcon from '@lucide/svelte/icons/code';
import HexagonIcon from '@lucide/svelte/icons/hexagon';
import CoffeeIcon from '@lucide/svelte/icons/coffee';
import ZapIcon from '@lucide/svelte/icons/zap';

export interface GuideStep {
	heading: string;
	body?: string;
	code?: { text: string; label?: string };
}

export interface GuideItem {
	id: string;
	title: string;
	icon: Component;
	intro: string;
	steps: GuideStep[];
}

export interface GuideCategory {
	id: string;
	label: string;
	itemIds: string[];
}

/**
 * Endpoints as the browser sees them - `host` is `window.location.hostname` (the same
 * origin this dashboard was loaded from), which is a correct guess for the common
 * docker-compose deployment (dashboard/api/ingest all on one host, different ports per
 * docker-compose.yml) but not a guarantee for every topology. The Kubernetes item
 * deliberately does *not* use these - a cluster-internal Service DNS name has nothing to
 * do with the browser's origin, so that one stays a placeholder.
 */
export interface GuideEndpoints {
	/** host:port, no scheme - what OTel Collector exporter config (`endpoint:`) and Go's otlploggrpc.WithEndpoint want. */
	grpcHostPort: string;
	/** http://host:port - what OTEL_EXPORTER_OTLP_ENDPOINT env vars and C#'s `new Uri(...)` want. */
	grpcUri: string;
	/** http://host:port - the HTTP/protobuf or HTTP/JSON base; callers append /v1/logs themselves. */
	httpUri: string;
	/** Flare.Api's own origin (same one this dashboard already calls) - only used by the optional ingest-key step. */
	apiOrigin: string;
}

function buildItems(ep: GuideEndpoints): Record<string, GuideItem> {
	return {
		kubernetes: {
			id: 'kubernetes',
			title: 'Kubernetes',
			icon: BoxesIcon,
			intro:
				"Run the OpenTelemetry Collector as a DaemonSet so it tails every pod's container logs and forwards them to Flare.Ingest over OTLP.",
			steps: [
				{
					heading: 'Add the OpenTelemetry Helm repo',
					code: {
						text: 'helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts\nhelm repo update'
					}
				},
				{
					heading: 'Point the collector at Flare.Ingest',
					body: 'Replace the endpoint below with wherever Flare.Ingest is reachable from inside the cluster - a Service DNS name if it runs in-cluster too, an external host:port otherwise.',
					code: {
						label: 'values.yaml',
						text: `mode: daemonset
presets:
  logsCollection:
    enabled: true
config:
  exporters:
    otlp/flare:
      endpoint: <flare-ingest-host>:4317
      tls:
        insecure: true
  service:
    pipelines:
      logs:
        exporters: [otlp/flare]`
					}
				},
				{
					heading: 'Install',
					code: { text: 'helm install otel-collector open-telemetry/opentelemetry-collector -f values.yaml' }
				}
			]
		},
		docker: {
			id: 'docker',
			title: 'Docker',
			icon: ContainerIcon,
			intro:
				'Already have an OTLP exporter in your app? Point it at Flare.Ingest with standard OTEL_EXPORTER_OTLP_* environment variables - no collector needed in between.',
			steps: [
				{
					heading: 'Same docker-compose network as Flare',
					body: '"ingest" is the service name Flare\'s own docker-compose.yml gives Flare.Ingest - use whatever name your Flare.Ingest container has, as long as both containers share a network.',
					code: {
						label: 'docker-compose.yml',
						text: `services:
  your-app:
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://ingest:4317
      OTEL_EXPORTER_OTLP_PROTOCOL: grpc
      OTEL_SERVICE_NAME: your-app
    networks:
      - flare`
					}
				},
				{
					heading: 'Running elsewhere?',
					body: 'Same variables work for a container (or any process) outside the compose network - point at the published host ports instead.',
					code: {
						text: `OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri}\nOTEL_EXPORTER_OTLP_PROTOCOL=grpc\nOTEL_SERVICE_NAME=your-app`
					}
				}
			]
		},
		linux: {
			id: 'linux',
			title: 'Linux',
			icon: TerminalIcon,
			intro:
				'Run the OpenTelemetry Collector to tail log files (or receive OTLP from local apps) and forward everything to Flare.',
			steps: [
				{
					heading: 'Download otelcol-contrib',
					code: {
						text: `curl -L -o otelcol-contrib.tar.gz \\
  https://github.com/open-telemetry/opentelemetry-collector-releases/releases/latest/download/otelcol-contrib_linux_amd64.tar.gz
tar -xzf otelcol-contrib.tar.gz otelcol-contrib
sudo mv otelcol-contrib /usr/local/bin/`
					}
				},
				{
					heading: 'Configure it',
					code: {
						label: '/etc/otelcol/config.yaml',
						text: `receivers:
  filelog:
    include: [/var/log/**/*.log]
exporters:
  otlp/flare:
    endpoint: ${ep.grpcHostPort}
    tls:
      insecure: true
service:
  pipelines:
    logs:
      receivers: [filelog]
      exporters: [otlp/flare]`
					}
				},
				{
					heading: 'Run it',
					body: 'Wrap this in a systemd unit for anything beyond a quick test.',
					code: { text: 'otelcol-contrib --config /etc/otelcol/config.yaml' }
				}
			]
		},
		windows: {
			id: 'windows',
			title: 'Windows',
			icon: MonitorIcon,
			intro:
				'Run the OpenTelemetry Collector as a local OTLP relay, or skip it entirely and point an app’s own exporter straight at Flare (see Languages & Frameworks) - same protocol either way.',
			steps: [
				{
					heading: 'Download & run the Collector',
					body: 'Grab otelcol-contrib_windows_amd64.tar.gz from the OpenTelemetry Collector releases page.',
					code: { text: 'otelcol-contrib.exe --config config.yaml' }
				},
				{
					heading: 'Configure it',
					body: 'Receives OTLP from apps on this machine and forwards to Flare - add the contrib windowseventlog receiver alongside otlp if you also want the Windows Event Log.',
					code: {
						label: 'config.yaml',
						text: `receivers:
  otlp:
    protocols:
      grpc:
      http:
exporters:
  otlp/flare:
    endpoint: ${ep.grpcHostPort}
    tls:
      insecure: true
service:
  pipelines:
    logs:
      receivers: [otlp]
      exporters: [otlp/flare]`
					}
				}
			]
		},
		dotnet: {
			id: 'dotnet',
			title: '.NET',
			icon: InfinityIcon,
			intro: 'The standard OpenTelemetry .NET SDK exports logs straight to Flare - no Flare-specific package required.',
			steps: [
				{
					heading: 'Add the packages',
					code: {
						text: 'dotnet add package OpenTelemetry.Extensions.Logging\ndotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol'
					}
				},
				{
					heading: 'Wire up the exporter',
					code: {
						label: 'Program.cs',
						text: `using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("your-service"));
    options.AddOtlpExporter(otlp =>
    {
        otlp.Endpoint = new Uri("${ep.grpcUri}");
    });
});`
					}
				},
				{
					heading: 'Using Aspire?',
					body: 'Flare ships an Aspire.Flare package that does the above for you, reading the endpoint from a connection string (e.g. .WithReference(flare) on the AppHost side) instead of a hardcoded URI.',
					code: {
						text: 'dotnet add package Aspire.Flare'
					}
				},
				{
					heading: '',
					code: { text: 'builder.AddFlareOtlpExporter("flare");' }
				}
			]
		},
		python: {
			id: 'python',
			title: 'Python',
			icon: CodeIcon,
			intro: "Zero-code instrumentation - wrap your app's start command, no source changes needed.",
			steps: [
				{
					heading: 'Install',
					code: {
						text: 'pip install opentelemetry-distro opentelemetry-exporter-otlp\nopentelemetry-bootstrap -a install'
					}
				},
				{
					heading: 'Run your app through it',
					code: {
						text: `OTEL_SERVICE_NAME=your-service \\
OTEL_LOGS_EXPORTER=otlp \\
OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri} \\
opentelemetry-instrument python app.py`
					}
				}
			]
		},
		nodejs: {
			id: 'nodejs',
			title: 'Node.js',
			icon: HexagonIcon,
			intro: 'Zero-code instrumentation via the auto-instrumentations meta-package.',
			steps: [
				{ heading: 'Install', code: { text: 'npm install --save @opentelemetry/auto-instrumentations-node' } },
				{
					heading: 'Run your app through it',
					code: {
						text: `OTEL_SERVICE_NAME=your-service \\
OTEL_LOGS_EXPORTER=otlp \\
OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri} \\
node --require @opentelemetry/auto-instrumentations-node/register app.js`
					}
				}
			]
		},
		java: {
			id: 'java',
			title: 'Java',
			icon: CoffeeIcon,
			intro: 'Zero-code instrumentation via the OpenTelemetry Java agent - attach it and set environment variables, no source changes.',
			steps: [
				{
					heading: 'Download the agent',
					code: {
						text: 'curl -L -o opentelemetry-javaagent.jar \\\n  https://github.com/open-telemetry/opentelemetry-java-instrumentation/releases/latest/download/opentelemetry-javaagent.jar'
					}
				},
				{
					heading: 'Run your app with it attached',
					code: {
						text: `OTEL_SERVICE_NAME=your-service \\
OTEL_LOGS_EXPORTER=otlp \\
OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri} \\
java -javaagent:opentelemetry-javaagent.jar -jar your-app.jar`
					}
				}
			]
		},
		go: {
			id: 'go',
			title: 'Go',
			icon: ZapIcon,
			intro: 'Go has no zero-code agent for logs yet - wire the SDK’s OTLP log exporter directly.',
			steps: [
				{
					heading: 'Add the modules',
					code: {
						text: 'go get go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc\ngo get go.opentelemetry.io/otel/sdk/log'
					}
				},
				{
					heading: 'Wire up the exporter',
					body: 'The Go logs SDK is newer than traces/metrics and its API has moved faster between releases - check go.opentelemetry.io/otel’s own docs if this doesn’t match your installed version.',
					code: {
						label: 'main.go',
						text: `exporter, err := otlploggrpc.New(context.Background(),
    otlploggrpc.WithEndpoint("${ep.grpcHostPort}"),
    otlploggrpc.WithInsecure(),
)
if err != nil {
    log.Fatal(err)
}

provider := sdklog.NewLoggerProvider(
    sdklog.WithProcessor(sdklog.NewBatchProcessor(exporter)),
)
global.SetLoggerProvider(provider)`
					}
				}
			]
		},
		custom: {
			id: 'custom',
			title: 'Custom / raw OTLP',
			icon: WebhookIcon,
			intro: 'Flare.Ingest speaks plain OTLP - point any OTLP-capable exporter, collector, or HTTP client at it directly.',
			steps: [
				{
					heading: 'Endpoints',
					body: `gRPC: ${ep.grpcHostPort} (opentelemetry.proto.collector.logs.v1.LogsService/Export) · HTTP: ${ep.httpUri}/v1/logs (application/json or application/x-protobuf)`
				},
				{
					heading: 'Try it with curl',
					code: {
						label: 'HTTP + JSON',
						text: `curl -s -X POST ${ep.httpUri}/v1/logs \\
  -H "Content-Type: application/json" \\
  -d '{"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"curl-test"}}]},"scopeLogs":[{"scope":{"name":"manual-test"},"logRecords":[{"timeUnixNano":"1700000000000000000","severityNumber":9,"severityText":"INFO","body":{"stringValue":"hello from curl"}}]}]}]}'`
					},
					body: 'Expect 200 {}. The event is buffered for a couple seconds before it lands in ClickHouse, so give Logs a moment before checking.'
				},
				{
					heading: 'If this deployment requires an ingest key',
					body: 'Anonymous ingest is the default. If yours has been turned to required, create a key from an Admin session and send it as a Bearer token on every export.',
					code: {
						label: 'Create a key (run as an Admin)',
						text: `curl -s -X POST ${ep.apiOrigin}/api/ingest-keys \\
  -H "Content-Type: application/json" \\
  --cookie "<your dashboard session cookie>" \\
  -d '{"name":"my-app"}'`
					}
				},
				{
					heading: '',
					code: {
						label: 'Then add the header to your exporter',
						text: 'OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer%20<your-key>'
					}
				}
			]
		}
	};
}

export function buildCategories(ep: GuideEndpoints): { categories: GuideCategory[]; items: Record<string, GuideItem> } {
	const items = buildItems(ep);
	const categories: GuideCategory[] = [
		{ id: 'recommended', label: 'Recommended', itemIds: ['kubernetes', 'docker', 'dotnet', 'custom'] },
		{ id: 'platforms', label: 'Platforms', itemIds: ['kubernetes', 'docker', 'linux', 'windows'] },
		{ id: 'languages', label: 'Languages & Frameworks', itemIds: ['dotnet', 'python', 'nodejs', 'java', 'go'] },
		{ id: 'custom', label: 'Custom', itemIds: ['custom'] }
	];
	return { categories, items };
}
