# Comment surveiller les nœuds et pods Kubernetes

Envoyez les métriques des nœuds et des pods de votre cluster à Flare avec les
récepteurs `kubeletstats` et `k8s_cluster` de l'OpenTelemetry Collector, et
consultez-les sur la page **Kubernetes** : un tableau **Nodes** et un tableau
**Pods**, chacun avec des graphiques détaillés.

La page **Kubernetes** est distincte de **Resources**. Resources interroge l'API
Kubernetes et ne liste que les pods de Flare lui-même. La page Kubernetes montre
tout votre cluster, tel que votre collecteur le rapporte via OTLP.

## Prérequis

- Une instance Flare en cours d'exécution, dont le port OTLP (`4317` gRPC ou
  `4318` HTTP) est joignable depuis le cluster.
- La distribution [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  (`otelcol-contrib`), déployée dans le cluster. Le
  [chart Helm OpenTelemetry](https://github.com/open-telemetry/opentelemetry-helm-charts)
  est le moyen le plus simple de la déployer.

## Configurer le collecteur

Les deux récepteurs rapportent des choses différentes, et chacun remplit des
colonnes différentes :

| Récepteur | S'exécute en tant que | Rapporte |
|---|---|---|
| `kubeletstats` | DaemonSet (un collecteur par nœud) | Utilisation CPU et mémoire des nœuds et des pods |
| `k8s_cluster` | Deployment à un seul réplica | État Ready des nœuds, CPU allouable, phase des pods, redémarrages des conteneurs |

Avec le chart Helm, le preset `kubeletMetrics` ajoute `kubeletstats` à un
collecteur DaemonSet, et le preset `clusterMetrics` ajoute `k8s_cluster` à un
collecteur Deployment. Vous pouvez n'en exécuter qu'un seul ; les colonnes
alimentées par l'autre affichent **—**.

### Collecteur DaemonSet (`kubeletstats`)

```yaml
receivers:
  kubeletstats:
    auth_type: serviceAccount
    endpoint: "https://${env:K8S_NODE_NAME}:10250"
    insecure_skip_verify: true
    collection_interval: 60s

processors:
  k8sattributes: {}

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # votre hôte Flare.Ingest
    tls:
      insecure: true                        # ou configurez TLS

service:
  pipelines:
    metrics:
      receivers: [kubeletstats]
      processors: [k8sattributes]
      exporters: [otlp]
```

Le processeur `k8sattributes` est recommandé. Il ajoute le nom du nœud de
chaque pod et sa charge de travail propriétaire (Deployment, StatefulSet,
DaemonSet, Job ou CronJob), que le tableau Pods affiche dans ses colonnes
**Node** et **Workload**.

### Collecteur Deployment (`k8s_cluster`)

```yaml
receivers:
  k8s_cluster:
    collection_interval: 60s
    allocatable_types_to_report: [cpu]

service:
  pipelines:
    metrics:
      receivers: [k8s_cluster]
      exporters: [otlp]
```

`allocatable_types_to_report: [cpu]` est facultatif. Sans lui, la colonne
**CPU % alloc.** reste vide.

Pour distinguer plusieurs clusters, définissez un attribut de ressource
`k8s.cluster.name` sur les deux collecteurs, par exemple avec le processeur
`resource`. Le tableau Nodes propose alors un filtre par cluster.

Si vous avez activé les [clés API d'ingestion](configure-authentication.fr.md#clés-api-dingestion),
ajoutez la clé aux `headers` de l'exportateur.

## Lire le tableau Nodes

Ouvrez **Kubernetes** dans la barre de navigation. Les nœuds apparaissent en
un intervalle de collecte.

| Colonne | Métrique source | Signification |
|---|---|---|
| Status | `k8s.node.condition_ready` | Le dernier état Ready rapporté |
| CPU | `k8s.node.cpu.usage` | CPU utilisé, moyenné sur la fenêtre, en millicœurs (`250m`) ou en cœurs |
| CPU % alloc. | `k8s.node.allocatable_cpu` | CPU en proportion du CPU allouable du nœud |
| Memory | `k8s.node.memory.working_set` | Working set mémoire, moyenné sur la fenêtre |
| Memory % | `k8s.node.memory.available` | Working set en proportion du working set plus la mémoire disponible |
| Pods | toute métrique `k8s.pod.*` | Pods distincts ayant rapporté sur ce nœud pendant la fenêtre |
| Last seen | toute métrique `k8s.node.*` | Dernier rapport du nœud |

Flare lit aussi `k8s.node.cpu.utilization`, l'ancien nom de la même valeur CPU,
qu'envoient les collecteurs plus anciens.

- **Explorez en détail** en cliquant sur le nom d'un nœud. Cela ouvre les
  graphiques de CPU, CPU %, mémoire et mémoire % sur la fenêtre sélectionnée.
- **Voyez les pods d'un nœud** en cliquant sur son nombre de **Pods**, ou sur
  **View pods on this node** dans le détail.

## Lire le tableau Pods

Passez à l'onglet **Pods**, ou ouvrez `/kubernetes?tab=pods`.

| Colonne | Métrique source | Signification |
|---|---|---|
| Workload | `k8s.deployment.name` et attributs de ressource similaires | Le Deployment, StatefulSet, DaemonSet, CronJob, Job ou ReplicaSet propriétaire |
| Node | `k8s.node.name` | Le nœud depuis lequel le pod a rapporté en dernier |
| Status | `k8s.pod.phase` | La dernière phase : Pending, Running, Succeeded, Failed ou Unknown |
| Restarts | `k8s.container.restarts` | Le dernier nombre de redémarrages, additionné sur les conteneurs du pod |
| CPU | `k8s.pod.cpu.usage` | CPU utilisé, moyenné sur la fenêtre, plus la part de la limite du pod si elle est rapportée |
| Memory | `k8s.pod.memory.working_set` | Working set mémoire, moyenné sur la fenêtre, plus la part de la limite du pod si elle est rapportée |

La part de la limite provient des métriques optionnelles
`k8s.pod.cpu_limit_utilization` et `k8s.pod.memory_limit_utilization` du
récepteur `kubeletstats`. Consultez [Comment surveiller des hôtes](monitor-hosts.fr.md#voir-les-métriques-dun-hôte-à-côté-dun-log)
pour les activer.

Filtrez les pods par nom (sous-chaîne, insensible à la casse), par namespace ou
par nœud. Cliquez sur le nom d'un pod pour voir ses graphiques CPU et mémoire.

## Limites et obsolescence

Un **—** signifie que Flare n'a reçu aucune donnée pour cette métrique dans la
fenêtre. Il n'est jamais affiché comme 0.

Un nœud ou un pod qui n'a pas rapporté depuis plus de cinq minutes est marqué
**stale**. Un pod supprimé reste dans la liste, marqué stale, jusqu'à ce qu'il
sorte de la fenêtre sélectionnée.

Le tableau Nodes liste jusqu'à 500 nœuds et le tableau Pods jusqu'à 1 000 pods.
Au-delà, un avis vous invite à affiner le filtre.

## Dépannage

**Aucun nœud ni pod n'apparaît.** Vérifiez les erreurs d'export dans les logs du
collecteur. Flare identifie les nœuds par l'attribut de ressource
`k8s.node.name` et les pods par `k8s.pod.name` plus `k8s.namespace.name` ; les
deux récepteurs les définissent par défaut.

**La colonne Node ou Workload est vide pour certains pods.** `kubeletstats` ne
définit pas lui-même le nœud ni le propriétaire d'un pod. Ajoutez le processeur
`k8sattributes`, ou exécutez le récepteur `k8s_cluster`, qui rapporte le nœud de
chaque pod.

**Status, Restarts et CPU % alloc. sont vides.** Ces valeurs proviennent du
récepteur `k8s_cluster`. Déployez-le comme décrit ci-dessus.
