# Référence de l'intégration d'hébergement Aspire

L'API exacte de `CommunityToolkit.Aspire.Hosting.Flare`, sa correspondance avec
`Flare.Hosting.Aspire` (obsolète), et l'API et les faits de déploiement propres
à ce package obsolète. Pour savoir comment l'utiliser, voir
[`../how-to/deploy/run-with-aspire.fr.md`](../how-to/deploy/run-with-aspire.fr.md).

| Package | Statut | Aspire | Source |
|---|---|---|---|
| `CommunityToolkit.Aspire.Hosting.Flare` | Actuel, `17.0.0` | 17+ (`Aspire.Hosting` 17.0.0) | [CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare) |
| `Flare.Hosting.Aspire` | Obsolète, dernière version `0.5.1` | 13.x | `src/Aspire.Hosting.Flare` dans ce dépôt |

## `CommunityToolkit.Aspire.Hosting.Flare`

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name,
    IResourceBuilder<ClickHouseServerResource> clickhouse,
    IResourceBuilder<RedisResource> redis,
    int? dashboardPort = null)
```

| Paramètre | Signification |
|---|---|
| `name` | Le nom de la ressource d'ingestion, qui sert aussi de nom de chaîne de connexion pour `WithReference`. Les ressources enfants s'appellent `{name}-api`, `{name}-alert-worker` et `{name}-dashboard`. |
| `clickhouse` | Le serveur ClickHouse où Flare stocke la télémétrie. `AddFlare` y ajoute une base `{name}-clickhousedb` ; les images migrent elles-mêmes leur schéma au démarrage. |
| `redis` | L'instance Redis qui met la télémétrie en tampon avant son écriture dans ClickHouse. |
| `dashboardPort` | Le port d'hôte du tableau de bord. Aléatoire si `null`. |

Ce qu'il exécute, depuis Docker Hub avec un tag commun (`0.6` par défaut) :

| Ressource | Image | Points de terminaison |
|---|---|---|
| `{name}` (la `FlareResource` renvoyée) | `apardev/flare-ingest` | `otlp-grpc` sur le port d'hôte `4317`, `otlp-http` sur `4318`, sans proxy |
| `{name}-api` | `apardev/flare-api` | HTTP |
| `{name}-alert-worker` | `apardev/flare-alert-worker` | HTTP (santé uniquement) |
| `{name}-dashboard` | `apardev/flare-dashboard` | HTTP sur `dashboardPort` |

Comme la ressource renvoyée est le conteneur d'ingestion lui-même,
`.WaitFor(flare)` attend que Flare accepte la télémétrie.

### Méthodes de la ressource Flare

| Méthode | Effet |
|---|---|
| `WithDataVolume(string? name = null)` | Conserve la base d'identité de Flare (utilisateurs du tableau de bord, sessions, clés d'API d'ingestion) dans un volume partagé par l'ingestion et l'API. Sans lui, chacun garde une base privée perdue quand son conteneur est recréé. Les volumes de ClickHouse et Redis se configurent sur ces ressources. |
| `WithIngestPorts(int? grpcPort = null, int? httpPort = null)` | Change les ports d'hôte OTLP. `null` laisse un port inchangé. |
| `WithApiKey(IResourceBuilder<ParameterResource> apiKey)` | Exige `Authorization: Bearer <clé>` à l'ingestion. Les ressources câblées avec `WithOtlpExporter(flare)` l'envoient automatiquement. |
| `WithPublicUrls(apiUrl, dashboardUrl)` | Paramètres des URL de l'API et du tableau de bord vues par le navigateur, nécessaires une fois publié. L'un ou l'autre peut être `null`. `dashboardUrl` sert tel quel d'origine CORS à l'API : `scheme://host[:port]`, sans chemin ni barre oblique finale. |
| `WithFlareImageTag(string tag)` | Le tag d'image des quatre conteneurs. |

### `WithOtlpExporter`

```csharp
IResourceBuilder<T> WithOtlpExporter<T>(
    this IResourceBuilder<T> builder,
    IResourceBuilder<FlareResource> flare,
    OtlpProtocol protocol = OtlpProtocol.Grpc)
    where T : IResourceWithEnvironment
```

Définit `OTEL_EXPORTER_OTLP_ENDPOINT` et `OTEL_EXPORTER_OTLP_PROTOCOL` sur la
ressource consommatrice.

| `OtlpProtocol` | Point de terminaison | `OTEL_EXPORTER_OTLP_PROTOCOL` |
|---|---|---|
| `Grpc` (par défaut) | `otlp-grpc` (`4317`) | `grpc` |
| `HttpProtobuf` | `otlp-http` (`4318`) | `http/protobuf` |
| `HttpJson` | `otlp-http` (`4318`) | `http/json` |

La télémétrie de la ressource part alors vers Flare au lieu du tableau de bord
Aspire. La clé OTLP du tableau de bord Aspire est retirée de
`OTEL_EXPORTER_OTLP_HEADERS` (les autres en-têtes sont conservés), et
`Authorization=Bearer <clé>` est ajouté quand `WithApiKey` est défini.

### Propriétés de connexion

`FlareResource` implémente `IResourceWithConnectionString`. Avec
`.WithReference(flare)`, le consommateur reçoit `ConnectionStrings__{name}`,
qui vaut l'URL OTLP/gRPC, plus ces propriétés sous forme de variables
`{NAME}_{PROPERTY}` (par exemple `FLARE_URI`) :

| Propriété | Valeur |
|---|---|
| `Host` | Hôte du point de terminaison OTLP/gRPC |
| `Port` | Port du point de terminaison OTLP/gRPC |
| `Uri` | L'URL OTLP/gRPC, par exemple `http://localhost:4317` |

C'est la chaîne de connexion que lit `AddFlareOtlpExporter(name)` du package
client `Flare.Aspire`, qui fonctionne donc avec l'un ou l'autre package
d'hébergement.

## Migrer depuis `Flare.Hosting.Aspire`

Passez d'abord l'AppHost à Aspire 17, puis remplacez la référence de package.

| `Flare.Hosting.Aspire` | `CommunityToolkit.Aspire.Hosting.Flare` |
|---|---|
| `AddFlare("flare")` crée ClickHouse et Redis | `AddFlare("flare", builder.AddClickHouse("clickhouse"), builder.AddRedis("redis"))` |
| Volumes ClickHouse/Redis/identité créés pour vous | `WithDataVolume()` sur les ressources ClickHouse et Redis, et sur Flare pour la base d'identité |
| `AddFlare(..., imageTag: "x")` | `WithFlareImageTag("x")` |
| `WithIngestGrpcPort(p)` / `WithIngestHttpPort(p)` | `WithIngestPorts(grpcPort: p, httpPort: p)` |
| `WithDashboardPort(p)` | `AddFlare(..., dashboardPort: p)` |
| `WithPublicApiUrl(a)` / `WithPublicDashboardUrl(d)` | `WithPublicUrls(a, d)` |
| `WithApiKey(key)` | `WithApiKey(key)` ; `WithOtlpExporter` l'envoie désormais pour vous |
| `WaitForFlare(flare)` | `WaitFor(flare)` |
| `WithOtlpEndpoint(flare)` | `WithOtlpExporter(flare)` |
| `WithOtlpEndpoint(flare, useHttp: true)` | `WithOtlpExporter(flare, OtlpProtocol.HttpProtobuf)` |
| `WithReference(flare)` + `AddFlareOtlpExporter("flare")` | Inchangé |

Pas encore d'équivalent : `WithApiPort`, `WithIngestImage`/`WithApiImage`/
`WithDashboardImage`/`WithAlertWorkerImage`, `WithPersistentStorage`,
`WithColdStorage` et `enableResourceGraph`. Gardez `Flare.Hosting.Aspire`
0.5.1 si vous dépendez de l'un d'eux.

Changements de comportement à vérifier :

- **Variables de l'exportateur.** `WithOtlpEndpoint` ne définissait que
  `OTEL_EXPORTER_OTLP_ENDPOINT`. `WithOtlpExporter` définit aussi
  `OTEL_EXPORTER_OTLP_PROTOCOL`, retire la clé OTLP du tableau de bord Aspire et
  ajoute la clé Flare. Si vous définissiez vous-même le protocole pour
  `useHttp: true`, supprimez-le et passez un `OtlpProtocol`.
- **Visibilité.** L'ancien package masquait ses ressources sous-jacentes dans
  le tableau de bord Aspire ; le toolkit affiche ClickHouse, Redis et les
  quatre conteneurs Flare.
- **Schéma.** L'ancien package embarquait `db/clickhouse/*.sql` et construisait
  une image d'initialisation ClickHouse ; les images du toolkit migrent le
  schéma elles-mêmes, aucune image n'est donc construite à la publication.

## `Flare.Hosting.Aspire` (obsolète)

ID de package `Flare.Hosting.Aspire` (et non `Aspire.Hosting.Flare` : ce
préfixe est réservé sur nuget.org), projet `src/Aspire.Hosting.Flare`. Marqué
obsolète sur nuget.org au profit du package du toolkit ; `0.5.1` est la
dernière version et reste installable pour Aspire 13.x.

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "0.6.0",
    bool enableResourceGraph = false)
```

Seuls les trois éléments qui décident de *ce qui est construit* (un conteneur
sidecar ou des objets RBAC, dans le cas d'`enableResourceGraph`) sont des
paramètres du constructeur ; tout le reste est une méthode `With*` chaînée sur
le builder `FlareResource` renvoyé.

| Paramètre | Signification |
|---|---|
| `name` | Le nom du groupe de ressources Flare dans le tableau de bord Aspire. |
| `imageTag` | Par défaut, la dernière version stable de Flare testée avec cette version du package (`"0.6.0"` dans 0.5.1), tirée des tags immuables `v*.*.*` de Docker Hub. Passez `imageTag: "edge"` pour suivre la branche `main` non publiée. Les méthodes `With*Image` réutilisent ce tag. |
| `enableResourceGraph` | Active la page Resources du tableau de bord. Désactivé par défaut. Sous Docker, ajoute un sidecar docker-socket-proxy en lecture seule ([ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)) ; sous Kubernetes, un `ServiceAccount`/`Role`/`RoleBinding` restreint ([ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)). Désactivé, `flare-api` n'a aucun accès à Docker ni au cluster et la page affiche un état « non activé ». |

### Méthodes chaînées `With*`

| Méthode | Ce qu'elle remplace |
|---|---|
| `WithIngestGrpcPort(int)` / `WithIngestHttpPort(int)` | Les ports d'hôte du récepteur OTLP. Par défaut `4317`/`4318`. Toujours sans proxy. |
| `WithApiPort(int)` / `WithDashboardPort(int)` | Les ports d'hôte de l'API de requête / du tableau de bord. Points de terminaison HTTP Aspire ordinaires avec proxy. |
| `WithIngestImage(string)` / `WithApiImage(string)` / `WithDashboardImage(string)` / `WithAlertWorkerImage(string)` | Nom/registre de l'image (pas le tag, fourni par `imageTag`), par exemple des images construites localement avec `docker compose build`. |
| `WithApiKey(IResourceBuilder<ParameterResource>)` | Exige une clé d'API d'ingestion. Pas de transmission automatique : un projet qui appelle `AddFlareOtlpExporter` a encore besoin de la valeur brute dans son délégué `configureSettings: s => s.ApiKey = ...`. |
| `WithPublicApiUrl(...)` / `WithPublicDashboardUrl(...)` | Remplacent les URL vues par le navigateur, fixées sur `localhost` (`PUBLIC_API_URL`/`Cors__AllowedOrigins__0`). Nécessaire seulement à la publication/au déploiement. |
| `WithPersistentStorage(clickHouseVolume, redisVolume, identityVolume)` | Lie trois résultats de `kubernetesEnvironment.AddPersistentVolume(...)` au stockage de ClickHouse, de Redis et de la base d'identité ; voir [Kubernetes](#kubernetes). Marquée `[Experimental("ASPIRECOMPUTE002")]`, comme `AddPersistentVolume`. |
| `WithColdStorage(accessKey?, secretKey?, bucket = "flare-cold")` | Ajoute un conteneur RustFS (compatible S3), une tâche ponctuelle qui crée le bucket et la politique de stockage ClickHouse `flare_tiered`, pour que la rétention puisse déplacer les données anciennes vers un stockage objet ([ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)). Rien ne bouge tant que vous ne définissez pas de délai avec `PUT /api/retention`. Passez vos propres clés hors usage local. |
| `WaitForFlare(flare)` | Attend la ressource du tableau de bord. Un simple `.WaitFor(flare)` n'attend pas, car la `FlareResource` de ce package est un nœud de regroupement sans cycle de vie. |
| `WithOtlpEndpoint(flare, useHttp: false)` | Définit seulement `OTEL_EXPORTER_OTLP_ENDPOINT`, sur le point de terminaison gRPC d'ingestion ou, avec `useHttp: true`, le HTTP. |

### Faits de déploiement

À connaître avant de déployer (pas `aspire run`) un AppHost qui utilise ce
package. Docker Compose et Kubernetes ont été vérifiés à partir de `0.2.3` ;
les cibles Azure/AWS ne le sont pas.

#### Docker Compose

- `publicApiUrl`/`publicDashboardUrl` ne sont pas définis par défaut.
  `aspire run` suppose que le navigateur est sur la même machine et pointe
  `PUBLIC_API_URL`/`Cors__AllowedOrigins__0` vers `localhost`. S'ils restent
  vides, Aspire les capture comme espaces réservés `.env.{environment}` à
  remplir par environnement.
- L'initialisation de ClickHouse construit une petite image personnalisée à
  la publication (`FROM` l'image résolue par `AddClickHouse`, plus les scripts
  d'initialisation copiés par `COPY`) plutôt qu'un montage bind, car le chemin
  source du montage n'existe que sur la machine qui a lancé `aspire publish`.
  Un `aspire run` local nécessite donc aussi de pouvoir lancer `docker build`.
- Le docker-socket-proxy d'`enableResourceGraph` a besoin d'un vrai hôte
  Docker : son montage de `/var/run/docker.sock` exige que la machine qui lance
  `docker compose up` dispose de ce socket.

#### Kubernetes

- **Un registre de conteneurs est nécessaire, mais uniquement à cause de
  l'image d'initialisation ClickHouse.** Les quatre images Flare viennent
  directement de Docker Hub ; seule l'image d'initialisation ClickHouse générée
  est construite et poussée par `aspire deploy` vers le registre configuré via
  `AddContainerRegistry`/`WithContainerRegistry`.
- **Les données ClickHouse/Redis/identité NE survivent PAS au redémarrage d'un
  pod sans `WithPersistentStorage`.** Les volumes du package sont rendus en
  simples `emptyDir: {}` tant que vous ne liez pas un vrai
  [`AddPersistentVolume`](https://aspire.dev/deployment/kubernetes/persistent-volumes/)
  à chacun de `{name}-clickhouse-data`, `{name}-redis-data` et
  `{name}-identity-data` :

    ```csharp
    var k8s = builder.AddKubernetesEnvironment("k8s");
    var flare = builder.AddFlare("flare")
        .WithPersistentStorage(
            clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
                .WithStorageClass("standard").WithCapacity("20Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            redisVolume: k8s.AddPersistentVolume("flare-redis-data")
                .WithStorageClass("standard").WithCapacity("5Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            identityVolume: k8s.AddPersistentVolume("flare-identity-data")
                .WithStorageClass("standard").WithCapacity("1Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));
    ```

    Vérifié sur un cluster k3s à un seul nœud (`aspire deploy`) : les PVC se
    lient, ClickHouse et Redis survivent à la suppression d'un pod avec leurs
    données, et le PVC d'identité partagé fonctionne monté par les pods
    d'ingestion et d'API. Non vérifié : ce PVC partagé avec l'ingestion et l'API
    sur deux nœuds différents, où la plupart des pilotes de stockage bloc
    exigeraient `ReadWriteMany`. Voir
    [l'investigation](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md),
    constat n° 8.
  - Omettre `WithPersistentStorage` avec un environnement Kubernetes enregistré
    journalise un avertissement, mais la CLI de pipeline d'Aspire ne l'écrit que
    dans `~/.aspire/logs/cli_*.log`, pas dans la console (constat n° 7). Fiez-vous
    à cette page.
- `publicApiUrl`/`publicDashboardUrl` sont tout aussi nécessaires que pour
  Docker Compose. Non définis, ils pointent vers des noms DNS de Service internes
  au cluster (par exemple `http://flare-api-service:8080`), inaccessibles depuis
  un navigateur.
- **`ImagePullPolicy.Always`** est défini sur les images ingest/api/dashboard,
  mais rendu en `IfNotPresent` dans les manifestes générés lors de la
  vérification du 2026-08-29 ; cause non trouvée. Ne compte qu'avec
  `imageTag: "edge"`.
- **La page Resources fonctionne aussi sur Kubernetes**, via un fournisseur
  distinct restreint par RBAC (ADR-0006) : `flare-api` liste les Pods étiquetés
  Flare (`flare.role`) et les Services de l'espace de noms, et affiche Namespace
  → groupes « Deployment » synthétisés → Pod, plus des nœuds Service avec des
  arêtes `Selects`.
