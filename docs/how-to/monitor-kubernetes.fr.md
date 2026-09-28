# Comment surveiller des clusters Kubernetes

Envoyez les métriques de votre cluster à Flare avec les récepteurs
`kubeletstats` et `k8s_cluster` de l'OpenTelemetry Collector, et consultez-les
sur la page **Kubernetes**. La page comporte cinq onglets : **Nodes**,
**Namespaces**, **Workloads** (Deployments, StatefulSets, DaemonSets, Jobs et
CronJobs), **Pods** et **Volumes**. Les nœuds, charges de travail, pods et
volumes ont chacun des graphiques détaillés.

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
| `kubeletstats` | DaemonSet (un collecteur par nœud) | Utilisation CPU et mémoire des nœuds et des pods, utilisation des volumes des pods |
| `k8s_cluster` | Deployment à un seul réplica | État Ready des nœuds, CPU allouable, phase des pods, redémarrages des conteneurs, nombres de réplicas et de jobs des charges de travail, phase des namespaces |

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
    metric_groups: [node, pod, container, volume]

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
**Node** et **Workload**. Le tableau Workloads en a aussi besoin pour compter
les pods d'une charge de travail et additionner leur CPU et leur mémoire.

`metric_groups` est facultatif. Le groupe `volume` est désactivé par défaut ;
ajoutez-le pour remplir l'onglet **Volumes**.

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

**View logs**, dans le détail d'un pod, ouvre **Logs** filtré sur les attributs
de ressource `k8s.namespace.name` et `k8s.pod.name` de ce pod. Seuls les logs
qui portent ces attributs sont trouvés : exécutez aussi le processeur
`k8sattributes` dans votre pipeline de logs.

## Lire le tableau Namespaces

Passez à l'onglet **Namespaces**, ou ouvrez `/kubernetes?tab=namespaces`. Un
namespace apparaît dès qu'une métrique `k8s.*` porte son `k8s.namespace.name`.

| Colonne | Métrique source | Signification |
|---|---|---|
| Status | `k8s.namespace.phase` | Active ou Terminating |
| Pods | toute métrique `k8s.pod.*` | Pods distincts ayant rapporté dans le namespace pendant la fenêtre |
| CPU, Memory | `k8s.pod.cpu.usage`, `k8s.pod.memory.working_set` | Utilisation totale des pods du namespace (voir ci-dessous) |

Cliquez sur un namespace pour ouvrir l'onglet Pods filtré sur celui-ci. Les
onglets Workloads, Pods et Volumes partagent un même filtre de namespace.

## Lire le tableau Workloads

Passez à l'onglet **Workloads**, ou ouvrez `/kubernetes?tab=workloads`, et
choisissez un type. La colonne **Status** dépend du type :

| Type | Status | Métriques sources |
|---|---|---|
| Deployment | Réplicas disponibles sur désirés | `k8s.deployment.available`, `k8s.deployment.desired` |
| StatefulSet | Pods prêts sur désirés | `k8s.statefulset.ready_pods`, `k8s.statefulset.desired_pods` |
| DaemonSet | Nœuds prêts sur désirés | `k8s.daemonset.ready_nodes`, `k8s.daemonset.desired_scheduled_nodes` |
| Job | Pods réussis sur désirés, pods en échec, pods actifs | `k8s.job.successful_pods`, `k8s.job.desired_successful_pods`, `k8s.job.failed_pods`, `k8s.job.active_pods` |
| CronJob | Jobs actifs | `k8s.cronjob.active_jobs` |

Toutes ces valeurs proviennent du récepteur `k8s_cluster` et affichent la
dernière lecture de la fenêtre. Flare lit aussi les noms plus récents des
conventions sémantiques, comme `k8s.deployment.pod.desired`. Triez par
**Status** pour placer en tête les charges de travail les plus dégradées : le
plus de réplicas manquants, ou le plus de pods en échec.

**Pods**, **CPU** et **Memory** proviennent des pods qui portent le nom de la
charge de travail, ajouté par le processeur `k8sattributes`. Le CPU et la
mémoire sont l'utilisation totale des pods : additionnée sur les pods à chaque
instant, puis moyennée sur la fenêtre. Les anciens et nouveaux pods d'une mise à
jour progressive ne s'additionnent que lorsqu'ils tournent réellement ensemble.

Cliquez sur le nom d'une charge de travail pour voir les graphiques de ses
compteurs, de son CPU et de sa mémoire. **View pods** ouvre l'onglet Pods filtré
sur cette charge de travail. Les pods d'un Job sont trouvés même lorsqu'un
CronJob possède ce Job.

## Lire le tableau Volumes

Passez à l'onglet **Volumes**, ou ouvrez `/kubernetes?tab=volumes`. Chaque ligne
est un volume monté par un pod, issu du groupe de métriques `volume` du
récepteur `kubeletstats`.

| Colonne | Métrique source | Signification |
|---|---|---|
| Volume | `k8s.volume.name`, `k8s.persistentvolumeclaim.name` | Le volume, et sa réclamation pour un volume adossé à un PVC |
| Type | `k8s.volume.type` | Par exemple `persistentVolumeClaim`, `emptyDir` ou `configMap` |
| Used, Capacity | `k8s.volume.capacity`, `k8s.volume.available` | Capacité moins espace disponible, et capacité |
| Used % | les mêmes | Espace utilisé en proportion de la capacité |
| Inodes % | `k8s.volume.inodes`, `k8s.volume.inodes.used` (ou `.free`) | Inodes utilisés en proportion de tous les inodes |

Les valeurs des volumes sont la dernière lecture de la fenêtre, pas une
moyenne : un volume qui se remplit affiche son niveau actuel. Le tableau est
trié par **Used %**, le plus plein en premier. La recherche porte sur le nom du
volume ou de la réclamation. Cliquez sur un volume pour voir les graphiques de
ses octets utilisés, de son pourcentage utilisé et de ses inodes.

## Limites et obsolescence

Un **—** signifie que Flare n'a reçu aucune donnée pour cette métrique dans la
fenêtre. Il n'est jamais affiché comme 0.

Un nœud ou un pod qui n'a pas rapporté depuis plus de cinq minutes est marqué
**stale**. Un pod supprimé reste dans la liste, marqué stale, jusqu'à ce qu'il
sorte de la fenêtre sélectionnée.

Les tableaux Nodes et Namespaces listent jusqu'à 500 lignes ; les tableaux
Workloads, Pods et Volumes jusqu'à 1 000. Au-delà, un avis vous invite à affiner
le filtre.

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

**Le Status d'une charge de travail est vide, mais ses Pods, CPU et Memory sont
remplis.** La charge de travail n'a été trouvée que par les attributs de ses
pods. Déployez le récepteur `k8s_cluster` pour les nombres de réplicas et de
jobs.

**Les Pods, CPU et Memory d'une charge de travail sont vides.** Ses pods ne
portent pas son nom. Ajoutez le processeur `k8sattributes` au pipeline
`kubeletstats`.

**L'onglet Volumes est vide.** Ajoutez `volume` aux `metric_groups` du récepteur
`kubeletstats`.
