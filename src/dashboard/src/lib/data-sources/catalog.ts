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
// One deliberate exception to "everything here is logs": the "prometheus" item (Metrics
// tab) documents Flare.Ingest's native Prometheus scrape receiver (Planning.md v20,
// PrometheusScrapeWorker/PrometheusScrapeOptions) - the one source in this catalog that's
// metrics, not logs, and pull (Flare reaches out to the target), not push (nothing here
// points an exporter *at* Flare). Kept in this same catalog/page rather than a second
// "how do I send metrics" page since there's exactly one such item - not enough to justify
// a parallel guide, and this page is the only ingestion-help surface that exists.
//
// Ingest auth: IngestApiKeyValidationMiddleware (src/Flare.Ingest/Auth/) only enforces a
// Bearer token when IngestAuthOptions.IngestKeyRequired is turned on - off by default, so
// every snippet below is written for the common anonymous-ingest case. The "custom" item's
// last step covers the opt-in key case without building key-management UI here. Prometheus
// scrape has no such step - it's server-side config, not an exporter credential, so
// IngestApiKeyValidationMiddleware never applies to it at all.

import type { Component } from 'svelte';
import * as m from '$lib/paraglide/messages';
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
// Prometheus's own logo is a flame/torch - closest generic-icon match, same "closest
// generic icon to the real thing" convention the language icons above already use.
import FlameIcon from '@lucide/svelte/icons/flame';

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
			title: m.dataSourceCatalog_kubernetesTitle(),
			icon: BoxesIcon,
			intro: m.dataSourceCatalog_kubernetesIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_kubernetesStep1Heading(),
					code: {
						text: 'helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts\nhelm repo update'
					}
				},
				{
					heading: m.dataSourceCatalog_kubernetesStep2Heading(),
					body: m.dataSourceCatalog_kubernetesStep2Body(),
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
					heading: m.dataSourceCatalog_kubernetesStep3Heading(),
					code: { text: 'helm install otel-collector open-telemetry/opentelemetry-collector -f values.yaml' }
				}
			]
		},
		docker: {
			id: 'docker',
			title: m.dataSourceCatalog_dockerTitle(),
			icon: ContainerIcon,
			intro: m.dataSourceCatalog_dockerIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_dockerStep1Heading(),
					body: m.dataSourceCatalog_dockerStep1Body(),
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
					heading: m.dataSourceCatalog_dockerStep2Heading(),
					body: m.dataSourceCatalog_dockerStep2Body(),
					code: {
						text: `OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri}\nOTEL_EXPORTER_OTLP_PROTOCOL=grpc\nOTEL_SERVICE_NAME=your-app`
					}
				}
			]
		},
		linux: {
			id: 'linux',
			title: m.dataSourceCatalog_linuxTitle(),
			icon: TerminalIcon,
			intro: m.dataSourceCatalog_linuxIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_linuxStep1Heading(),
					code: {
						text: `curl -L -o otelcol-contrib.tar.gz \\
  https://github.com/open-telemetry/opentelemetry-collector-releases/releases/latest/download/otelcol-contrib_linux_amd64.tar.gz
tar -xzf otelcol-contrib.tar.gz otelcol-contrib
sudo mv otelcol-contrib /usr/local/bin/`
					}
				},
				{
					heading: m.dataSourceCatalog_linuxStep2Heading(),
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
					heading: m.dataSourceCatalog_linuxStep3Heading(),
					body: m.dataSourceCatalog_linuxStep3Body(),
					code: { text: 'otelcol-contrib --config /etc/otelcol/config.yaml' }
				}
			]
		},
		windows: {
			id: 'windows',
			title: m.dataSourceCatalog_windowsTitle(),
			icon: MonitorIcon,
			intro: m.dataSourceCatalog_windowsIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_windowsStep1Heading(),
					body: m.dataSourceCatalog_windowsStep1Body(),
					code: { text: 'otelcol-contrib.exe --config config.yaml' }
				},
				{
					heading: m.dataSourceCatalog_windowsStep2Heading(),
					body: m.dataSourceCatalog_windowsStep2Body(),
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
			title: m.dataSourceCatalog_dotnetTitle(),
			icon: InfinityIcon,
			intro: m.dataSourceCatalog_dotnetIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_dotnetStep1Heading(),
					code: {
						text: 'dotnet add package OpenTelemetry.Extensions.Logging\ndotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol'
					}
				},
				{
					heading: m.dataSourceCatalog_dotnetStep2Heading(),
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
					heading: m.dataSourceCatalog_dotnetStep3Heading(),
					body: m.dataSourceCatalog_dotnetStep3Body(),
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
			title: m.dataSourceCatalog_pythonTitle(),
			icon: CodeIcon,
			intro: m.dataSourceCatalog_pythonIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_pythonStep1Heading(),
					code: {
						text: 'pip install opentelemetry-distro opentelemetry-exporter-otlp\nopentelemetry-bootstrap -a install'
					}
				},
				{
					heading: m.dataSourceCatalog_pythonStep2Heading(),
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
			title: m.dataSourceCatalog_nodejsTitle(),
			icon: HexagonIcon,
			intro: m.dataSourceCatalog_nodejsIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_nodejsStep1Heading(),
					code: { text: 'npm install --save @opentelemetry/auto-instrumentations-node' }
				},
				{
					heading: m.dataSourceCatalog_nodejsStep2Heading(),
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
			title: m.dataSourceCatalog_javaTitle(),
			icon: CoffeeIcon,
			intro: m.dataSourceCatalog_javaIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_javaStep1Heading(),
					code: {
						text: 'curl -L -o opentelemetry-javaagent.jar \\\n  https://github.com/open-telemetry/opentelemetry-java-instrumentation/releases/latest/download/opentelemetry-javaagent.jar'
					}
				},
				{
					heading: m.dataSourceCatalog_javaStep2Heading(),
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
			title: m.dataSourceCatalog_goTitle(),
			icon: ZapIcon,
			intro: m.dataSourceCatalog_goIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_goStep1Heading(),
					code: {
						text: 'go get go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc\ngo get go.opentelemetry.io/otel/sdk/log'
					}
				},
				{
					heading: m.dataSourceCatalog_goStep2Heading(),
					body: m.dataSourceCatalog_goStep2Body(),
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
			title: m.dataSourceCatalog_jenkinsTitle(),
			icon: WrenchIcon,
			intro: m.dataSourceCatalog_jenkinsIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_jenkinsStep1Heading(),
					body: m.dataSourceCatalog_jenkinsStep1Body()
				},
				{
					heading: m.dataSourceCatalog_jenkinsStep2Heading(),
					body: m.dataSourceCatalog_jenkinsStep2Body(),
					code: {
						label: 'receivers.filelog (rest of config.yaml matches the Linux item)',
						text: `receivers:
  filelog:
    include: [/var/log/jenkins/jenkins.log]`
					}
				},
				{
					heading: m.dataSourceCatalog_jenkinsStep3Heading(),
					body: m.dataSourceCatalog_jenkinsStep3Body(),
					code: {
						text: `OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri}\nOTEL_EXPORTER_OTLP_PROTOCOL=grpc`
					}
				}
			]
		},
		ansible: {
			id: 'ansible',
			title: m.dataSourceCatalog_ansibleTitle(),
			icon: WorkflowIcon,
			intro: m.dataSourceCatalog_ansibleIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_ansibleStep1Heading(),
					code: { text: 'ansible-galaxy collection install community.general' }
				},
				{
					heading: m.dataSourceCatalog_ansibleStep2Heading(),
					body: m.dataSourceCatalog_ansibleStep2Body(),
					code: {
						text: `ANSIBLE_CALLBACKS_ENABLED=community.general.opentelemetry \\
OTEL_EXPORTER_OTLP_ENDPOINT=${ep.grpcUri} \\
OTEL_EXPORTER_OTLP_PROTOCOL=grpc \\
ansible-playbook site.yml`
					}
				},
				{
					heading: m.dataSourceCatalog_ansibleStep3Heading(),
					body: m.dataSourceCatalog_ansibleStep3Body()
				}
			]
		},
		terraform: {
			id: 'terraform',
			title: m.dataSourceCatalog_terraformTitle(),
			icon: LayersIcon,
			intro: m.dataSourceCatalog_terraformIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_terraformStep1Heading(),
					code: { text: 'TF_LOG=DEBUG\nTF_LOG_PATH=terraform.log\nterraform apply' }
				},
				{
					heading: m.dataSourceCatalog_terraformStep2Heading(),
					body: m.dataSourceCatalog_terraformStep2Body()
				},
				{
					heading: m.dataSourceCatalog_terraformStep3Heading(),
					body: m.dataSourceCatalog_terraformStep3Body()
				}
			]
		},
		'github-actions': {
			id: 'github-actions',
			title: m.dataSourceCatalog_githubActionsTitle(),
			icon: CirclePlayIcon,
			intro: m.dataSourceCatalog_githubActionsIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_githubActionsStep1Heading(),
					body: m.dataSourceCatalog_githubActionsStep1Body()
				},
				{
					heading: m.dataSourceCatalog_githubActionsStep2Heading(),
					body: m.dataSourceCatalog_githubActionsStep2Body(),
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
			title: m.dataSourceCatalog_vectorTitle(),
			icon: MoveRightIcon,
			intro: m.dataSourceCatalog_vectorIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_vectorStep1Heading(),
					body: m.dataSourceCatalog_vectorStep1Body(),
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
			title: m.dataSourceCatalog_fluentBitTitle(),
			icon: DropletIcon,
			intro: m.dataSourceCatalog_fluentBitIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_fluentBitStep1Heading(),
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
			title: m.dataSourceCatalog_syslogTitle(),
			icon: RadioTowerIcon,
			intro: m.dataSourceCatalog_syslogIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_syslogStep1Heading(),
					body: m.dataSourceCatalog_syslogStep1Body(),
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
					body: m.dataSourceCatalog_syslogStep2Body()
				}
			]
		},
		prometheus: {
			id: 'prometheus',
			title: m.dataSourceCatalog_prometheusTitle(),
			icon: FlameIcon,
			intro: m.dataSourceCatalog_prometheusIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_prometheusStep1Heading(),
					body: m.dataSourceCatalog_prometheusStep1Body(),
					code: {
						label: 'appsettings.json',
						text: `{
  "PrometheusScrape": {
    "Targets": [
      {
        "Job": "node-exporter",
        "Url": "http://node-exporter:9100/metrics",
        "Interval": "00:00:15",
        "Timeout": "00:00:10"
      }
    ]
  }
}`
					}
				},
				{
					heading: m.dataSourceCatalog_prometheusStep2Heading(),
					body: m.dataSourceCatalog_prometheusStep2Body(),
					code: {
						label: 'docker-compose.yml',
						text: `services:
  ingest:
    environment:
      PrometheusScrape__Targets__0__Job: node-exporter
      PrometheusScrape__Targets__0__Url: http://node-exporter:9100/metrics`
					}
				},
				{
					heading: m.dataSourceCatalog_prometheusStep3Heading(),
					body: m.dataSourceCatalog_prometheusStep3Body(),
					code: {
						label: 'appsettings.json (one target, extended)',
						text: `{
  "Job": "node-exporter",
  "Url": "http://node-exporter:9100/metrics",
  "Headers": { "Authorization": "Bearer <token>" },
  "Labels": { "service.name": "my-custom-name" }
}`
					}
				},
				{
					heading: m.dataSourceCatalog_prometheusStep4Heading(),
					body: m.dataSourceCatalog_prometheusStep4Body()
				}
			]
		},
		custom: {
			id: 'custom',
			title: m.dataSourceCatalog_customTitle(),
			icon: WebhookIcon,
			intro: m.dataSourceCatalog_customIntro(),
			steps: [
				{
					heading: m.dataSourceCatalog_customStep1Heading(),
					body: m.dataSourceCatalog_customStep1Body({ grpcHostPort: ep.grpcHostPort, httpUri: ep.httpUri })
				},
				{
					heading: m.dataSourceCatalog_customStep2Heading(),
					code: {
						label: 'HTTP + JSON',
						text: `curl -s -X POST ${ep.httpUri}/v1/logs \\
  -H "Content-Type: application/json" \\
  -d '{"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"curl-test"}}]},"scopeLogs":[{"scope":{"name":"manual-test"},"logRecords":[{"timeUnixNano":"1700000000000000000","severityNumber":9,"severityText":"INFO","body":{"stringValue":"hello from curl"}}]}]}]}'`
					},
					body: m.dataSourceCatalog_customStep2Body()
				},
				{
					heading: m.dataSourceCatalog_customStep3Heading(),
					body: m.dataSourceCatalog_customStep3Body(),
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
		{ id: 'recommended', label: m.dataSourceCatalog_categoryRecommended(), itemIds: ['kubernetes', 'docker', 'dotnet', 'custom'] },
		{ id: 'platforms', label: m.dataSourceCatalog_categoryPlatforms(), itemIds: ['kubernetes', 'docker', 'linux', 'windows'] },
		{ id: 'shippers', label: m.dataSourceCatalog_categoryShippers(), itemIds: ['vector', 'fluent-bit', 'syslog'] },
		{ id: 'metrics', label: m.dataSourceCatalog_categoryMetrics(), itemIds: ['prometheus'] },
		{
			id: 'languages',
			label: m.dataSourceCatalog_categoryLanguages(),
			itemIds: ['dotnet', 'python', 'nodejs', 'java', 'go']
		},
		{ id: 'devops', label: m.dataSourceCatalog_categoryDevops(), itemIds: ['jenkins', 'ansible', 'terraform', 'github-actions'] },
		{ id: 'custom', label: m.dataSourceCatalog_categoryCustom(), itemIds: ['custom'] }
	];
	return { categories, items };
}
