// Content for /data-sources (the "how do I send logs to Flare" guide, reached from the
// Logs empty state - see routes/data-sources/+page.svelte). Deliberately not a 1:1 port
// of OpenObserve's page: OpenObserve accepts an arbitrary per-vendor bulk-JSON API, so it
// can list 40+ individual integrations. Flare.Ingest only speaks OTLP (gRPC :4317, HTTP
// :4318 POST /v1/logs - see src/Flare.Ingest/README.md), so the honest equivalent for
// "Databases"/"Web Servers"/etc. is "run the OpenTelemetry Collector with that source's
// receiver, export OTLP to Flare" rather than a bespoke snippet per product. This is the
// curated "focused core" set: platforms apps commonly run on, the languages/SDKs most
// likely to be emitting the logs in the first place, the DevOps tools that came up by
// name (Jenkins/Ansible/Terraform/GitHub Actions), and the log shippers whose *own*
// native OTLP output can skip the Collector middleman entirely (Vector, Fluent Bit -
// verified against vector.dev/docs.fluentbit.io directly, since OpenObserve's own
// integration docs default both of them to OpenObserve's proprietary bulk-JSON API
// instead of OTLP). Not full category parity with OpenObserve's 40+ item catalog.
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
// Same "closest generic icon to the real thing" reasoning for the DevOps tab.
import WrenchIcon from '@lucide/svelte/icons/wrench';
import WorkflowIcon from '@lucide/svelte/icons/workflow';
import LayersIcon from '@lucide/svelte/icons/layers';
import CirclePlayIcon from '@lucide/svelte/icons/circle-play';
// Log shippers - Vector and Fluent Bit both have their own native OTLP output, verified
// against vector.dev/docs and docs.fluentbit.io directly (not just OpenObserve's
// integration page, which defaults both of them to OpenObserve's own proprietary
// bulk-JSON API instead - see this tab's own doc comment below).
import MoveRightIcon from '@lucide/svelte/icons/move-right';
import DropletIcon from '@lucide/svelte/icons/droplet';
import RadioTowerIcon from '@lucide/svelte/icons/radio-tower';

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
	/** Bare hostname, no port/scheme - what Fluent Bit's split Host/Port config keys want. */
	host: string;
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
		jenkins: {
			id: 'jenkins',
			title: 'Jenkins',
			icon: WrenchIcon,
			intro: "Jenkins doesn't speak OTLP itself - which approach fits depends on how it's deployed.",
			steps: [
				{
					heading: 'Running in a container?',
					body: 'Nothing Jenkins-specific needed - the Kubernetes/Docker items on the Platforms tab already tail container stdout/stderr and forward it as OTLP.'
				},
				{
					heading: 'Running on a VM or bare metal?',
					body: 'Same idea as the Platforms tab\'s Linux/Windows items - point the Collector\'s filelog receiver at Jenkins\' own log file instead of /var/log/**/*.log (the default location varies by install: a .deb/.rpm package writes to /var/log/jenkins/jenkins.log; running the .war directly just logs to stdout, so tail wherever that\'s redirected to).',
					code: {
						label: 'receivers.filelog (rest of config.yaml matches the Linux item)',
						text: `receivers:
  filelog:
    include: [/var/log/jenkins/jenkins.log]`
					}
				},
				{
					heading: 'Want per-build traces instead of raw log lines?',
					body: "Jenkins' official OpenTelemetry plugin exports each pipeline run as OTel traces (one span per stage/step) via OTLP directly - no Collector needed. It's built on the OTel Java SDK, so it reads the same standard environment variables; whether it also ships build console logs (not just traces) depends on your installed plugin version's own config screen (Manage Jenkins → Configure System → OpenTelemetry).",
					code: {
						text: `OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri}\nOTEL_EXPORTER_OTLP_PROTOCOL=grpc`
					}
				}
			]
		},
		ansible: {
			id: 'ansible',
			title: 'Ansible',
			icon: WorkflowIcon,
			intro: 'A playbook run is ephemeral, not a long-running service - the community.general collection ships an OpenTelemetry callback plugin that exports each run as an OTel trace (one span per task) instead.',
			steps: [
				{
					heading: 'Install the collection',
					code: { text: 'ansible-galaxy collection install community.general' }
				},
				{
					heading: 'Enable the callback and point it at Flare',
					body: 'Double-check these env var names against your installed community.general version - callback plugin configuration has shifted across releases.',
					code: {
						text: `ANSIBLE_CALLBACKS_ENABLED=community.general.opentelemetry \\
OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri} \\
OTEL_EXPORTER_OTLP_PROTOCOL=grpc \\
ansible-playbook site.yml`
					}
				},
				{
					heading: 'Running via AWX/Tower, or just want the raw log?',
					body: 'Treat it like any other Linux service and tail its log file instead - see the Platforms tab.'
				}
			]
		},
		terraform: {
			id: 'terraform',
			title: 'Terraform',
			icon: LayersIcon,
			intro: "A terraform apply run is even more ephemeral than a playbook, and the CLI has no OTLP exporter of its own - get its debug log onto disk, then forward that.",
			steps: [
				{
					heading: "Turn on Terraform's own debug log",
					code: { text: 'TF_LOG=DEBUG\nTF_LOG_PATH=terraform.log\nterraform apply' }
				},
				{
					heading: 'On a persistent host',
					body: "Tail terraform.log the same way the Platforms tab's Linux item tails any other log file - point the Collector's filelog receiver at it."
				},
				{
					heading: 'In ephemeral CI (e.g. a GitHub-hosted runner)',
					body: "There's no host left for a Collector to read from once the job ends - see the GitHub Actions item for shipping a run's outcome directly from the workflow instead."
				}
			]
		},
		'github-actions': {
			id: 'github-actions',
			title: 'GitHub Actions',
			icon: CirclePlayIcon,
			intro: 'No native OTLP exporter for Actions - the reliable option is a workflow step that ships the job\'s outcome directly, the same shape as the Custom tab\'s raw curl example.',
			steps: [
				{
					heading: 'Self-hosted runners you control?',
					body: 'Treat the runner like any other Linux/Windows host - see the Platforms tab. Nothing GitHub-specific needed.'
				},
				{
					heading: 'GitHub-hosted runners',
					body: "The runner disappears once the job ends, so add a step that reports the outcome directly instead, using GitHub's own workflow context. This is a minimal example (one log event per run) - free-text values like a commit message would need real JSON escaping (e.g. via jq) before going in the body.",
					code: {
						label: '.github/workflows/*.yml',
						text: `- name: Report to Flare
  if: always()
  env:
    STATUS: \${{ job.status }}
  run: |
    TS=$(date +%s%N)
    SEV=9
    [ "$STATUS" = "success" ] || SEV=17
    BODY=$(cat <<EOF
    {"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"github-actions"}}]},"scopeLogs":[{"scope":{"name":"\${{ github.workflow }}"},"logRecords":[{"timeUnixNano":"$TS","severityNumber":$SEV,"severityText":"$STATUS","body":{"stringValue":"\${{ github.workflow }} #\${{ github.run_number }} - $STATUS"}}]}]}]}
    EOF
    )
    curl -s -X POST ${ep.httpUri}/v1/logs -H "Content-Type: application/json" -d "$BODY"`
					}
				}
			]
		},
		vector: {
			id: 'vector',
			title: 'Vector',
			icon: MoveRightIcon,
			intro: "Vector ships a native OpenTelemetry sink - point it at Flare.Ingest directly, no separate Collector needed.",
			steps: [
				{
					heading: 'Add an opentelemetry sink',
					body: 'inputs should list whatever source or transform is already producing the events you want shipped - a file source tailing a log, a docker_logs source, etc.',
					code: {
						label: 'vector.yaml',
						text: `sinks:
  flare:
    type: opentelemetry
    inputs: [your_source_or_transform_id]
    protocol:
      type: http
      uri: ${ep.httpUri}/v1/logs
      encoding:
        codec: otlp`
					}
				}
			]
		},
		'fluent-bit': {
			id: 'fluent-bit',
			title: 'Fluent Bit',
			icon: DropletIcon,
			intro: 'Fluent Bit ships a built-in OpenTelemetry output plugin - point it at Flare.Ingest directly, no separate Collector needed.',
			steps: [
				{
					heading: 'Add an opentelemetry output',
					code: {
						label: 'fluent-bit.conf',
						text: `[OUTPUT]
    Name       opentelemetry
    Match      *
    Host       ${ep.host}
    Port       4318
    Logs_uri   /v1/logs`
					}
				}
			]
		},
		syslog: {
			id: 'syslog',
			title: 'Syslog',
			icon: RadioTowerIcon,
			intro: "Flare.Ingest has no syslog listener of its own - route through the OpenTelemetry Collector's syslog receiver instead, same pattern as any other source that doesn't speak OTLP.",
			steps: [
				{
					heading: 'Configure the Collector',
					body: "Runs alongside whatever else the Collector is already doing on this host - see the Platforms tab for the full install steps (this is just the receiver/exporter pair to add to that config).",
					code: {
						label: 'config.yaml',
						text: `receivers:
  syslog:
    tcp:
      listen_address: "0.0.0.0:5514"
    protocol: rfc5424
exporters:
  otlp/flare:
    endpoint: ${ep.grpcHostPort}
    tls:
      insecure: true
service:
  pipelines:
    logs:
      receivers: [syslog]
      exporters: [otlp/flare]`
					}
				},
				{
					heading: '',
					body: "Point whatever's emitting syslog (a network device, syslog-ng, journald's syslog forwarding, etc.) at this host on port 5514 instead of wherever it was going before."
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
		{ id: 'shippers', label: 'Log Shippers', itemIds: ['vector', 'fluent-bit', 'syslog'] },
		{ id: 'languages', label: 'Languages & Frameworks', itemIds: ['dotnet', 'python', 'nodejs', 'java', 'go'] },
		{ id: 'devops', label: 'DevOps', itemIds: ['jenkins', 'ansible', 'terraform', 'github-actions'] },
		{ id: 'custom', label: 'Custom', itemIds: ['custom'] }
	];
	return { categories, items };
}
