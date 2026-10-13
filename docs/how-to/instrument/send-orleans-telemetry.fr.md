# Comment envoyer la télémétrie d'Orleans à Flare

Voyez ce que font vos silos [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)
dans Flare : chaque appel de grain devient un span dans vos traces, les
compteurs d'exécution d'Orleans arrivent sur la page **Metrics**, et les
journaux que vos grains écrivent sont liés à l'appel qui les a produits.

Orleans émet lui-même des données OpenTelemetry : il n'y a ni agent, ni sidecar,
ni collecteur à faire tourner. Vous enregistrez les sources d'activité et le
meter d'Orleans auprès du SDK OpenTelemetry et vous exportez en OTLP.

## Prérequis

- Une instance Flare en cours d'exécution ([autonome](../deploy/run-standalone.fr.md),
  [Aspire](../deploy/run-with-aspire.fr.md) ou [CLI](../deploy/run-with-cli.fr.md)), dont le port OTLP
  gRPC (`4317`) est joignable depuis vos silos et clients.
- Un silo Orleans sur .NET. Cette page a été vérifiée de bout en bout avec
  Orleans 10.4.0 sur .NET 10 et OpenTelemetry 1.19.

## Envoyer traces, métriques et journaux

Ajoutez les paquets OpenTelemetry au projet du silo :

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Activez ensuite la propagation d'activité sur le silo et enregistrez les
sources et le meter :

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var flare = new Uri("http://flare.example.internal:4317");   // your Flare.Ingest host

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()        // or your real clustering
    .AddActivityPropagation());      // required: without it Orleans emits no spans

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-silo"))
    .WithTracing(t => t
        .AddSource(Orleans.Diagnostics.ActivitySources.ApplicationGrainActivitySourceName)
        .AddOtlpExporter(o => o.Endpoint = flare))
    .WithMetrics(m => m
        .AddMeter("Microsoft.Orleans")
        .AddOtlpExporter(o => o.Endpoint = flare));

builder.Logging.AddOpenTelemetry(l => l.AddOtlpExporter(o => o.Endpoint = flare));
```

Si vous exécutez aussi un client Orleans dans un processus distinct (un
frontal web, par exemple), appelez également `AddActivityPropagation()` sur son
générateur de client et donnez-lui son propre `service.name`. Les appels du
client et les exécutions de grains du silo partagent alors une même trace.

Si vous avez activé les [clés API d'ingestion](../admin/configure-authentication.fr.md#clés-api-dingestion),
ajoutez la clé à chaque exporteur avec `o.Headers = "Authorization=Bearer <key>"`.

## Ce que vous voyez dans Flare

### Traces

Chaque appel de grain produit deux spans dans la même trace, nommés
`<InterfaceDuGrain>/<Méthode>`, par exemple `IOrderGrain/Place` :

| Type de span | Signification |
| --- | --- |
| Client | Le côté appelant : un autre grain, ou votre code via `IGrainFactory`. |
| Serveur | L'exécution de la méthode du grain. |

Un appel d'un grain à un autre s'imbrique dans le span serveur du premier appel :
une requête qui se ramifie entre grains se lit donc de haut en bas dans la
cascade de la trace. Les spans portent `rpc.system.name = orleans`,
`orleans.rpc.service` (l'interface du grain), `orleans.rpc.target_id` (le type
et la clé du grain, comme `inventory/sku-2`) et `orleans.rpc.source_id` (le
grain appelant).

Une méthode de grain qui lève une exception donne aux deux spans un statut
d'erreur, avec `exception.type`, `exception.message` et `exception.stacktrace`
comme attributs de span. Ils comptent dans le taux d'erreur du service sur
**Traces > Services**.

### Métriques

Le meter `Microsoft.Orleans` exporte les mesures propres à l'exécution sous
forme de métriques comme `orleans-catalog-activations`, `orleans-grains`,
`orleans-app-requests-latency` et la famille `orleans-directory-*`. Un seul silo
en a exporté 42, et elles apparaissent dans le catalogue de métriques sous le
`service.name` du silo. Le SDK OpenTelemetry exporte les métriques toutes les
60 secondes par défaut : les premiers points peuvent mettre jusqu'à une minute
à apparaître.

### Journaux

Les journaux écrits via `ILogger` dans un grain portent l'identifiant de trace
et de span de l'appel en cours : vous pouvez passer d'une ligne de journal à sa
trace, et inversement. Les journaux d'Orleans lui-même (démarrage et arrêt du
silo, par exemple) sont également exportés.

## Choisir les sources d'activité

`ApplicationGrainActivitySourceName` ne donne que les appels de grains, et c'est
ce qu'il vous faut en production. Orleans a d'autres sources, et les ajouter
change beaucoup le volume :

- `Orleans.Diagnostics.ActivitySources.AllActivitySourceName` s'abonne à tout.
  Dans l'exécution vérifiée, la plupart des spans étaient des détails internes
  (activation et désactivation de grains, enregistrements dans l'annuaire,
  lectures de la table d'appartenance), et l'arrêt du silo ajoutait à lui seul
  une paire de spans pour chaque grain actif.
- `LifecycleActivitySourceName`, `StorageActivitySourceName` et
  `RuntimeActivitySourceName` ajoutent chacune une tranche de cela. Activez-en
  une pendant que vous analysez un problème précis, comme des activations
  lentes, puis retirez-la.

## Limites connues

- **Les échecs de grains n'apparaissent pas sur la page Errors.** Cette page lit
  les événements de span `exception`, alors qu'Orleans enregistre l'exception
  dans des attributs de span. Les appels en échec restent faciles à trouver :
  filtrez **Traces** par statut d'erreur.
- **Les appels de grain à grain ne dessinent pas de liens sur la carte des
  services.** Ils se produisent à l'intérieur d'un seul service (votre silo) :
  la carte affiche donc un seul nœud.
