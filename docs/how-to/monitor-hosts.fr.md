# Comment surveiller des hôtes avec l'OpenTelemetry Collector

Envoyez les métriques CPU, mémoire, disque et charge de vos machines à Flare
avec le récepteur `hostmetrics` de l'OpenTelemetry Collector, et consultez-les
sur la page **Hosts** : une ligne par hôte, avec un graphique détaillé pour
chaque métrique.

La page **Hosts** est distincte de **Resources**. Resources affiche
l'infrastructure que Flare découvre lui-même (conteneurs Docker, objets
Kubernetes et la machine sur laquelle Flare s'exécute). Hosts affiche les
machines qui envoient elles-mêmes leurs métriques à Flare via OTLP.

## Prérequis

- Une instance Flare en cours d'exécution ([autonome](run-standalone.fr.md),
  [Aspire](run-with-aspire.fr.md) ou [CLI](run-with-cli.fr.md)), dont le port
  OTLP (`4317` gRPC ou `4318` HTTP) est joignable depuis les hôtes à surveiller.
- La distribution [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  (`otelcol-contrib`) sur chaque hôte. La distribution de base n'inclut pas le
  processeur `resourcedetection`.

## Configurer le collecteur

Sur chaque hôte, pointez le collecteur vers Flare avec cette configuration :

```yaml
receivers:
  hostmetrics:
    collection_interval: 60s
    scrapers:
      cpu: {}
      memory: {}
      load: {}
      filesystem: {}

processors:
  resourcedetection:
    detectors: [system]

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # votre hôte Flare.Ingest
    tls:
      insecure: true                        # ou configurez TLS

service:
  pipelines:
    metrics:
      receivers: [hostmetrics]
      processors: [resourcedetection]
      exporters: [otlp]
```

Le processeur `resourcedetection` est obligatoire. Il définit les attributs de
ressource `host.name` et `os.type`, et Flare identifie les hôtes par
`host.name`. Les métriques qui ne le portent pas n'apparaissent pas sur la
page Hosts.

Si vous avez activé les [clés API d'ingestion](configure-authentication.fr.md#clés-api-dingestion),
ajoutez la clé aux `headers` de l'exportateur.

### Exécuter le collecteur dans un conteneur

Un collecteur conteneurisé signale les systèmes de fichiers du conteneur
lui-même, sauf si vous montez la racine de l'hôte et définissez `root_path` :

```yaml
receivers:
  hostmetrics:
    root_path: /hostfs
```

```bash
docker run -v /:/hostfs:ro --hostname "$(hostname)" ... otel/opentelemetry-collector-contrib
```

Sans cela, la colonne **Disk** reste vide (—). Passez aussi `--hostname`,
sinon `host.name` est l'identifiant du conteneur.

## Lire la page Hosts

Ouvrez **Hosts** dans la navigation supérieure. Les hôtes apparaissent en un
intervalle de collecte.

| Colonne | Métrique source | Signification |
|---|---|---|
| CPU | `system.cpu.time` | Part non inactive du temps CPU sur la fenêtre |
| Memory | `system.memory.usage` | Part `used` de la mémoire totale, moyennée sur la fenêtre |
| Disk | `system.filesystem.usage` | Part `used` de la capacité totale, additionnée sur tous les systèmes de fichiers signalés |
| Load (15m) | `system.cpu.load_average.15m` | Charge moyenne sur 15 minutes, moyennée sur la fenêtre |
| Last seen | toute métrique `system.*` | Dernier envoi de l'hôte |

Un **—** signifie que l'hôte n'a envoyé aucune donnée pour cette métrique dans
la fenêtre, par exemple parce que le scraper `filesystem` est désactivé. Il
n'est jamais affiché comme 0 %. Un hôte qui n'a rien envoyé depuis plus de
cinq minutes est marqué **stale**.

- **Filtrez** par nom d'hôte (sous-chaîne, insensible à la casse) ou par type
  d'OS.
- **Triez** en cliquant sur un en-tête de colonne.
- **Changez la fenêtre** avec le sélecteur de temps (5 minutes à 24 heures).
- **Explorez** un hôte en cliquant sur son nom : cela ouvre les graphiques des
  quatre métriques sur la fenêtre sélectionnée.

La page liste jusqu'à 500 hôtes. S'il y en a davantage, un avis vous invite à
affiner le filtre.

## Dépannage

**Un hôte n'apparaît pas.** Vérifiez les journaux du collecteur pour des
erreurs d'export, puis confirmez que ses métriques portent `host.name`. Un
hôte n'apparaît que s'il envoie au moins une métrique `system.*` dans la
fenêtre sélectionnée.

**CPU, Memory ou Load reste toujours vide.** Seules les métriques par défaut
du récepteur sont lues. Flare n'utilise pas les jauges optionnelles
`system.cpu.utilization`, `system.memory.utilization` ou
`system.filesystem.utilization` : assurez-vous que les scrapers `cpu`,
`memory` et `load` sont activés.
