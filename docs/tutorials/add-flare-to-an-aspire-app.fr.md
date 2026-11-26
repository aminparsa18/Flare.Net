# Tutoriel : ajouter Flare à une application .NET Aspire

À la fin de ce tutoriel, vous aurez une application .NET Aspire dont l'AppHost
démarre Flare à côté de vos services, avec les logs, traces et métriques de
deux services qui y arrivent, et vous aurez suivi une requête depuis le front
web jusque dans l'API, dans le tableau de bord de Flare. Comptez environ quinze
minutes.

Ce tutoriel diffère des précédents : il n'utilise pas la pile autonome
`docker compose`. Flare rejoint votre AppHost comme une ressource : il démarre,
s'arrête et se découvre comme tout le reste de votre application. Arrêtez
d'abord toute pile Flare autonome, car elle occuperait les mêmes ports (`4317`
et `4318`).

## Prérequis

- Le SDK .NET 10 ou 11
- La [CLI Aspire](https://aspire.dev), version 17 ou ultérieure
  (`aspire --version`)
- Docker Desktop, ou un autre moteur compatible Docker, en cours d'exécution

## 1. Créer une application Aspire

Créez le starter Aspire, un front web qui appelle une API, avec l'AppHost et
les service defaults partagés :

```sh
aspire new aspire-starter --name FlareAspire
cd FlareAspire
```

Vous devriez obtenir quatre projets : `FlareAspire.AppHost`,
`FlareAspire.ServiceDefaults`, `FlareAspire.ApiService` et `FlareAspire.Web`.
Si vous avez déjà une application Aspire 17, utilisez-la ; les étapes
ci-dessous ne touchent que l'AppHost.

## 2. Ajouter Flare à l'AppHost

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare --apphost FlareAspire.AppHost
```

Cela ajoute
[l'intégration Flare de l'Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare).
Dans `FlareAspire.AppHost/AppHost.cs`, ajoutez ClickHouse, Redis et Flare avant
vos services, puis connectez chaque service à Flare :

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

var apiService = builder.AddDotnetProject("apiservice", "../FlareAspire.ApiService/FlareAspire.ApiService.csproj")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.AddDotnetProject("webfrontend", "../FlareAspire.Web/FlareAspire.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.Build().Run();
```

Gardez vos autres appels tels quels. Les lignes ClickHouse, Redis et
`AddFlare`, `.WithOtlpExporter(flare)` et `.WaitFor(flare)` sont nouvelles.

- ClickHouse stocke les logs, traces et métriques de Flare, et Redis les met en
  tampon à l'arrivée. `WithDataVolume()` sur chacun conserve vos données d'une
  exécution à l'autre.
- `AddFlare` exécute le récepteur OTLP, l'API de requête, le worker d'alertes et
  le tableau de bord de Flare, tirés des images publiées. Son propre
  `WithDataVolume()` conserve les comptes utilisateurs et les clés d'API de
  Flare.
- `.WithOtlpExporter(flare)` pointe l'exportateur OpenTelemetry du service vers
  Flare. Les service defaults du starter exportent déjà en OTLP quand cette
  variable est définie, si bien que les services eux-mêmes ne changent pas.
- `.WaitFor(flare)` retient le service jusqu'à ce que Flare accepte la
  télémétrie, pour que les premiers logs ne soient pas perdus.

## 3. Lancer l'application

```sh
aspire start
aspire describe
```

La première exécution tire les images de Flare : prévoyez quelques minutes.
Quand chaque ressource affiche `Healthy`, `aspire describe` liste une URL pour
chacune. Il y a deux tableaux de bord :

- le **tableau de bord Aspire** (affiché en haut de la sortie), pour les
  ressources de votre application, leur état et leur sortie console ;
- le **tableau de bord de Flare**, l'URL de la ligne **`flare-dashboard`**.

Si le tableau de bord Aspire affiche une erreur de certificat dans votre
navigateur, voir
[`../how-to/deploy/run-with-aspire.fr.md`](../how-to/deploy/run-with-aspire.fr.md#le-tableau-de-bord-daspire-affiche-une-erreur-sslcertificat).

## 4. Générer une requête

Ouvrez l'URL de **`webfrontend`** et cliquez sur **Weather**. Cette page
demande une prévision au service d'API : c'est exactement le saut du front vers
le back qui mérite d'être tracé. Rechargez-la plusieurs fois.

## 5. La suivre dans Flare

Ouvrez le tableau de bord de Flare (l'URL de `flare-dashboard`) et choisissez
**Traces**.

- Vous devriez voir des traces pour `webfrontend` et `apiservice`. Ouvrez-en
  une qui commence à `webfrontend` : la cascade montre le span de requête du
  front, l'appel HTTP sortant et le span de l'API imbriqué dessous. Ils forment
  une seule trace parce que l'instrumentation du client HTTP envoie un en-tête
  `traceparent` que l'API reprend.
- Ouvrez l'onglet **Services** pour le débit de requêtes, le taux d'erreurs et
  la latence de chaque service, ainsi que la carte de qui appelle qui.
- Choisissez **Logs**. Les lignes des deux services y sont, étiquetées par
  service. Dépliez-en une pour voir son identifiant de trace et le lien
  **Logs for this trace**.
- Choisissez **Metrics** pour trouver les métriques runtime et ASP.NET Core que
  les service defaults émettent déjà, comme la durée des requêtes.

Les vues de télémétrie du tableau de bord Aspire restent vides pour ces deux
services : `WithOtlpExporter` envoie leur télémétrie à Flare à la place. Flare
la conserve après l'arrêt de l'application et y ajoute alertes, tableaux de
bord et rétention. Pour garder les deux vues, voir
[Envoyer la télémétrie de vos services à Flare](../how-to/deploy/run-with-aspire.fr.md#3-envoyer-la-télémétrie-de-vos-services-à-flare).

## 6. Arrêter l'application

```sh
aspire stop
```

## Ce que vous avez appris

- Qu'`AddFlare` place Flare dans un AppHost, au-dessus de ressources ClickHouse
  et Redis qui vous appartiennent.
- Que `WithOtlpExporter` et `WaitFor` y connectent un service sans toucher à son
  code.
- Que la télémétrie d'un service part alors vers Flare au lieu du tableau de
  bord Aspire.
- Comment suivre une requête à travers deux services dans une seule trace.

## Étapes suivantes

- Exiger une clé d'API d'ingestion, déployer le même AppHost, et les
  fonctionnalités encore propres à l'ancien package `Flare.Hosting.Aspire` :
  [`../how-to/deploy/run-with-aspire.fr.md`](../how-to/deploy/run-with-aspire.fr.md),
  et [`../reference/aspire-hosting.fr.md`](../reference/aspire-hosting.fr.md)
  pour toutes les options.
- Voir une application Aspire bien plus grande qui utilise Flare, avec Kafka,
  Postgres et un collector :
  [Tutoriel : déboguer un checkout lent de bout en bout](debug-a-slow-request.fr.md).
- Être prévenu quand quelque chose tourne mal dans vos services :
  [Tutoriel : votre première alerte](first-alert.fr.md).
