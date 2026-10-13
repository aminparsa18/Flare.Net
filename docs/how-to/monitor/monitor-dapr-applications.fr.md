# Comment surveiller des applications Dapr

Voyez ce que font vos sidecars [Dapr](https://dapr.io) dans Flare : les appels
d'invocation de service et de pub/sub apparaissent comme des spans dans vos
traces et sur la carte des services, et les métriques propres aux sidecars
arrivent à côté de celles de vos applications.

Dapr ne fait pas partie de Flare et Flare ne dépend pas de lui. Cette page
s'adresse aux applications qui tournent déjà sur Dapr et qui veulent un endroit
où envoyer sa télémétrie.

## Prérequis

- Une instance Flare en cours d'exécution ([autonome](../deploy/run-standalone.fr.md),
  [Aspire](../deploy/run-with-aspire.fr.md) ou [CLI](../deploy/run-with-cli.fr.md)), dont le port OTLP
  gRPC (`4317`) est joignable depuis les sidecars Dapr.
- Dapr avec prise en charge du traçage OTLP. Cette page a été vérifiée de bout
  en bout avec Dapr 1.18.7 en mode auto-hébergé. Les extraits Kubernetes
  utilisent la même ressource `Configuration` et n'ont pas été exécutés sur un
  cluster.

## Envoyer les traces des sidecars à Flare

Dapr écrit directement ses traces en OTLP, aucun collecteur n'est donc
nécessaire. Ajoutez un exporteur OTLP à la
[`Configuration`](https://docs.dapr.io/operations/configuration/configuration-overview/#tracing)
Dapr que vos applications utilisent :

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: flare
spec:
  tracing:
    samplingRate: "1"
    otel:
      endpointAddress: "flare.example.internal:4317"   # your Flare.Ingest host
      isSecure: false                                   # true if Ingest terminates TLS
      protocol: grpc
```

- **Définissez `samplingRate` explicitement.** La valeur par défaut de Dapr est
  de 1 sur 10 000, ce qui donne l'impression que Flare ne reçoit rien. `"1"`
  envoie toutes les traces. Pour limiter le volume en production, baissez-le
  ici ou utilisez l'[échantillonnage des traces à l'ingestion](../../explanation/sampling-clustering-and-cost.fr.md)
  côté Flare.
- **`endpointAddress` n'a pas de préfixe `http://` pour gRPC.** Indiquez
  uniquement l'hôte et le port. Avec `protocol: http`, visez le port `4318`.

Indiquez ensuite à chaque application d'utiliser cette configuration.

Sur Kubernetes, annotez le modèle de pod :

```yaml
annotations:
  dapr.io/enabled: "true"
  dapr.io/app-id: orders
  dapr.io/config: flare
```

En mode auto-hébergé, passez le fichier au sidecar :

```bash
daprd --app-id orders --app-port 6001 --resources-path components --config config.yaml
# or with the Dapr CLI: dapr run --app-id orders --config config.yaml ...
```

Si vous avez activé les [clés API d'ingestion](../admin/configure-authentication.fr.md#clés-api-dingestion),
la ressource `Configuration` n'a aucun champ pour les en-têtes de requête.
Faites alors passer les traces par un OpenTelemetry Collector et placez la clé
sur son exporteur.

## Ce que vous voyez dans Flare

Ouvrez **Traces** et filtrez par identifiant d'application. Cet identifiant
devient le nom du service (`service.name`).

| Span | Service | Signification |
| --- | --- | --- |
| `CallLocal/<app-id>/<method>` (client) | l'appelant | Invocation de service, vue par le sidecar appelant. |
| `CallLocal/<app-id>/<method>` (serveur) | l'appelé | Le même appel, reçu par le sidecar cible. |
| `/v1.0/publish/<pubsub>/<topic>` | l'éditeur | L'appel de l'API de publication au sidecar. |
| `pubsub/<topic>` | l'éditeur | Le sidecar remet le message au composant pub/sub. Porte `messaging.system` et `messaging.destination.name`. |

Les spans de l'appelant et de l'appelé partagent une trace : un appel de service
à service apparaît donc comme une seule trace avec les spans des deux sidecars.
**Traces > Services** dessine le lien de l'application appelante vers
l'application appelée, avec le nombre d'appels et la latence.

Les spans Dapr portent `dapr.api`, `dapr.protocol` et `dapr.status_code` selon
le cas, de sorte que vous pouvez les filtrer ou les regrouper comme n'importe
quel autre attribut.

## Envoyer les métriques des sidecars à Flare

Dapr n'expose ses métriques qu'au format Prometheus, sur le port `9090` de
chaque sidecar (modifiable avec `--metrics-port` ou l'annotation
`dapr.io/metrics-port`). Flare peut interroger ce point d'accès lui-même :
rien à installer. Ajoutez une cible par sidecar à Flare.Ingest :

```yaml
services:
  ingest:
    environment:
      PrometheusScrape__Targets__0__Job: orders
      PrometheusScrape__Targets__0__Url: http://orders-host:9090/
      PrometheusScrape__Targets__0__Interval: "00:00:15"
```

Le `Job` devient le `service.name` des métriques ; donnez-lui l'identifiant
d'application pour aligner les métriques sur les traces du sidecar. Consultez
**Data sources > Prometheus** dans le tableau de bord pour toutes les options
d'une cible.

Sur Kubernetes, les sidecars vont et viennent avec les pods : une liste de
cibles fixe ne convient pas. Utilisez un OpenTelemetry Collector avec le
récepteur `prometheus` et la découverte de services Kubernetes, limité aux pods
portant l'annotation `dapr.io/enabled`, et exportez vers Flare en OTLP.

Une fois collectées, des métriques comme `dapr_http_server_request_count`,
`dapr_component_pubsub_egress_count` et `dapr_component_pubsub_ingress_count`
apparaissent dans le catalogue de métriques.

Flare consigne un avertissement indiquant que `go_gc_duration_seconds` est
ignorée. Cette métrique est un résumé (summary) Prometheus, que Flare ne
stocke pas. Les autres métriques de la même collecte ne sont pas affectées.

## Limites connues

- **Le pub/sub de Dapr n'apparaît pas sur la page Messaging.** Dapr marque ses
  spans de publication comme des spans client et ne définit pas
  `messaging.operation`, alors que la page ne compte que les spans producteur
  et consommateur. Les spans restent présents dans vos traces. Pour les débits
  et latences par topic, utilisez les métriques des sidecars ci-dessus.
- **Les journaux des sidecars ne sont pas traités ici.** Les sidecars Dapr
  écrivent leurs journaux sur la sortie standard. Collectez-les comme le reste
  des journaux de vos conteneurs (voir **Data sources > Docker** ou
  **Kubernetes**). Démarrer `daprd` avec `--log-as-json` donne des champs
  structurés.
- **Uniquement les spans de Dapr.** Le sidecar ne trace pas le code de votre
  application. Ajoutez le SDK OpenTelemetry à votre application, avec le même
  point de terminaison Flare, pour obtenir des spans de votre propre code. Dapr
  propage le contexte de trace W3C : ils rejoignent donc les spans du sidecar
  dans la même trace.
