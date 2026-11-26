# Exécuter Flare depuis votre propre application .NET Aspire

Si votre application est déjà orchestrée avec .NET Aspire,
`CommunityToolkit.Aspire.Hosting.Flare` ajoute Flare à votre AppHost : le
service d'ingestion OTLP, l'API de requête, le worker d'alertes et le tableau
de bord, tirés des images Docker Hub publiées par Flare. Vous lui passez les
ressources ClickHouse et Redis dans lesquelles Flare stocke et met en tampon
ses données, si bien qu'elles restent visibles et configurables dans votre
modèle d'application. Pour l'API exacte, voir
[`../reference/aspire-hosting.fr.md`](../../reference/aspire-hosting.fr.md).

> **Nécessite Aspire 17.** Le package fait partie de
> l'[Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire) et
> dépend d'`Aspire.Hosting` 17.0.0. Sous Aspire 13.x, ou si vous avez besoin
> du stockage à froid, du stockage persistant Kubernetes ou de la page
> Resources, utilisez plutôt `Flare.Hosting.Aspire` 0.5.1, désormais obsolète :
> voir [Fonctionnalités pas encore dans le package du toolkit](#fonctionnalités-pas-encore-dans-le-package-du-toolkit).

## 1. Installer le package

Depuis le répertoire de votre AppHost :

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare
```

ou `dotnet add package CommunityToolkit.Aspire.Hosting.Flare`.

## 2. Ajouter Flare à votre AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

builder.Build().Run();
```

`AddFlare` crée la base de données dont il a besoin dans votre serveur
ClickHouse, et les images Flare migrent elles-mêmes leur schéma au démarrage.
La ressource `flare` est le service d'ingestion, sur les ports OTLP
conventionnels `4317` (gRPC) et `4318` (HTTP). L'API, le worker d'alertes et
le tableau de bord apparaissent en dessous sous les noms `flare-api`,
`flare-alert-worker` et `flare-dashboard`.

Les trois appels à `WithDataVolume()` conservent les données d'une exécution à
l'autre : la télémétrie de ClickHouse, le tampon de Redis et la base
d'identité de Flare (utilisateurs du tableau de bord, sessions et clés d'API
d'ingestion). Retirez-les pour une pile jetable.

## 3. Envoyer la télémétrie de vos services à Flare

```csharp
builder.AddDotnetProject("web", "../MyApp.Web/MyApp.Web.csproj")
    .WithOtlpExporter(flare)
    .WaitFor(flare);
```

`WithOtlpExporter(flare)` définit `OTEL_EXPORTER_OTLP_ENDPOINT` et
`OTEL_EXPORTER_OTLP_PROTOCOL` sur le service, si bien que les service defaults
Aspire standard (`UseOtlpExporter()`) envoient logs, traces et métriques à
Flare sans rien changer au service lui-même. Il utilise OTLP/gRPC par défaut.
Passez `OtlpProtocol.HttpProtobuf` ou `OtlpProtocol.HttpJson` pour utiliser
OTLP/HTTP.

**La télémétrie du service part vers Flare au lieu du tableau de bord
Aspire.** Celui-ci affiche toujours la ressource, sa sortie console et son
état, mais plus ses logs, traces ni métriques. Pour garder les deux :

- Utilisez `.WithReference(flare)` au lieu de `WithOtlpExporter`, et ajoutez
  Flare comme second exportateur dans le service avec le package client
  [`Flare.Aspire`](../../../src/Aspire.Flare/README.md)
  (`builder.AddFlareOtlpExporter("flare")`). Les service defaults doivent
  alors utiliser les appels `AddOtlpExporter()` propres à chaque signal plutôt
  que `UseOtlpExporter()` ; le tutoriel
  [Ajouter Flare à une application .NET Aspire](../../tutorials/add-flare-to-an-aspire-app.fr.md)
  montre la voie la plus simple.
- Ou exportez vers un OpenTelemetry Collector qui transmet aux deux.

## 4. Exiger une clé d'API d'ingestion (facultatif)

```csharp
var flareKey = builder.AddParameter("flare-api-key", secret: true);

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithApiKey(flareKey);
```

Les clients OTLP envoient alors `Authorization: Bearer <clé>`. Les services
câblés avec `WithOtlpExporter(flare)` l'envoient automatiquement.
`WithOtlpExporter` retire aussi la clé OTLP du tableau de bord Aspire de
`OTEL_EXPORTER_OTLP_HEADERS`, si bien que cette clé n'atteint jamais Flare.

## Publication / déploiement

Flare se déploie comme le reste de votre AppHost : ajoutez un environnement
de déploiement tel que `AddDockerComposeEnvironment("env")` ou
`AddKubernetesEnvironment("k8s")`, puis lancez `aspire publish` ou
`aspire deploy`. Voir [aspire.dev/deployment](https://aspire.dev/deployment/).

Dès que le tableau de bord n'est plus ouvert sur `localhost`, définissez les
URL qu'utilise un navigateur :

```csharp
var apiUrl = builder.AddParameter("flare-public-api-url");
var dashboardUrl = builder.AddParameter("flare-public-dashboard-url");

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithPublicUrls(apiUrl, dashboardUrl);
```

`dashboardUrl` est aussi l'origine CORS autorisée par l'API : passez donc une
origine seule, comme `https://flare.example.com`, sans chemin ni barre oblique
finale.

Le stockage se configure sur les ressources ClickHouse et Redis que vous avez
passées, comme pour n'importe quelle autre application Aspire. Sur Kubernetes,
le package du toolkit ne lie pas encore de volumes persistants pour la base
d'identité de Flare ; voir la section suivante.

## Fonctionnalités pas encore dans le package du toolkit

Elles n'existent que dans `Flare.Hosting.Aspire` 0.5.1, obsolète, qui
fonctionne aussi sous Aspire 13.x :

| Fonctionnalité | Dans `Flare.Hosting.Aspire` 0.5.1 |
|---|---|
| Stockage à froid (niveau RustFS pour la rétention) | `WithColdStorage()` |
| Volumes persistants Kubernetes pour ClickHouse, Redis et la base d'identité | `WithPersistentStorage(...)` |
| La page Resources du tableau de bord | `AddFlare(..., enableResourceGraph: true)` |
| Remplacement d'image ou de port par conteneur | `WithIngestImage`, `WithApiPort` et les autres |

Comment les utiliser, et comment passer de ce package à celui du toolkit :
[`../reference/aspire-hosting.fr.md`](../../reference/aspire-hosting.fr.md#flarehostingaspire-obsolète).

Pour une démo complète exécutable, voir [`../../examples/`](../../../examples) :
une boutique multi-services avec Kafka, Postgres et un OpenTelemetry
Collector, qui alimente en données réelles toutes les pages du tableau de bord
sauf Kubernetes. Elle utilise encore `Flare.Hosting.Aspire`, parce que son
déploiement Kubernetes a besoin de `WithPersistentStorage`.

## Dépannage

### Le tableau de bord d'Aspire affiche une erreur SSL/certificat

Vous pouvez voir des erreurs `RemoteCertificateNameMismatch` dans la console
de l'AppHost, et le **tableau de bord d'orchestration Aspire** (l'interface
Resources/Console/Traces à l'URL du tableau de bord de l'AppHost) ne pas
charger sa liste de ressources. Il s'agit d'un
[bug amont d'Aspire 13.4.6](https://aspire.dev/app-host/certificate-configuration/),
qui ne vient ni de Flare ni de votre AppHost : un composant interne d'Aspire se
connecte à son propre service de ressources via l'adresse IP de bouclage
`127.0.0.1`, mais le certificat de développement ASP.NET Core ne porte que des
SAN de noms DNS (`localhost`, `*.dev.internal`, etc.) et aucun SAN d'IP, si
bien que la validation stricte du nom d'hôte TLS le rejette. Le contournement
documenté `ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` ne le corrige pas sur
toutes les machines (il peut empêcher DCP de démarrer) : consultez les issues
GitHub d'Aspire avant de l'essayer.

**Cela n'affecte pas Flare.** Le tableau de bord de Flare, à l'URL de
`flare-dashboard`, est une application distincte (SvelteKit qui parle à
`flare-api` en HTTP simple, sans gRPC ni TLS) et fonctionne indépendamment de
ce bug. Si vous le rencontrez, utilisez `aspire ps` / `aspire describe` /
`aspire logs` pour suivre l'orchestration, et ouvrez directement le tableau de
bord de Flare.

### Le service d'ingestion ne démarre pas : port déjà utilisé

`4317` et `4318` sont des ports d'hôte fixes, pour que les clients OTLP
externes puissent utiliser les numéros conventionnels. Une pile Flare autonome
(`docker compose up` ou `flare start`) occupe les mêmes ports ; arrêtez-la
d'abord, ou déplacez ceux de l'AppHost avec
`WithIngestPorts(grpcPort, httpPort)`.
