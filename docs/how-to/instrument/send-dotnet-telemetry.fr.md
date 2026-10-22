# Comment envoyer la télémétrie d'une application .NET à Flare

Envoyez les journaux, traces et métriques d'une application ASP.NET Core ou d'un
service worker à Flare en un seul appel, avec le paquet `Flare.OpenTelemetry`. C'est
une fine couche au-dessus du SDK OpenTelemetry .NET standard : pas d'agent, pas de
format propriétaire, et vous pouvez toujours ajouter n'importe quelle autre
configuration OpenTelemetry à côté.

Si votre application s'exécute dans un AppHost .NET Aspire, utilisez plutôt
[`Flare.Aspire`](../deploy/run-with-aspire.fr.md) : il lit l'adresse injectée par
`.WithReference(flare)`. Cette page concerne toutes les autres applications .NET.

## Prérequis

- Une instance Flare en cours d'exécution ([autonome](../deploy/run-standalone.fr.md),
  [CLI](../deploy/run-with-cli.fr.md) ou [Aspire](../deploy/run-with-aspire.fr.md)) dont le
  port OTLP est joignable depuis votre application : `4317` pour gRPC, `4318` pour HTTP.
- Une application .NET 10.

## Ajouter le paquet et l'appel

```bash
dotnet add package Flare.OpenTelemetry
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareTelemetry("http://localhost:4317");
```

C'est toute la configuration. Elle enregistre :

- Un exportateur OTLP pour les journaux, les traces et les métriques.
- L'instrumentation ASP.NET Core et `HttpClient` : les requêtes et les appels
  sortants deviennent des spans, et les métriques correspondantes sont exportées.
- Les métriques du runtime .NET : ramasse-miettes, pool de threads, exceptions.

Les journaux écrits via `ILogger` portent l'identifiant de trace et de span de la
requête en cours, ce qui permet de passer d'une ligne de journal à sa trace dans
Flare, et inversement.

## Configurer

Chaque paramètre a une valeur par défaut. Ne définissez que ce dont vous avez besoin :

```csharp
builder.AddFlareTelemetry(o =>
{
    o.Endpoint = new Uri("https://flare.example.com:4318");
    o.Protocol = OtlpExportProtocol.HttpProtobuf;
    o.ApiKey = "<clé d'ingestion>";
    o.ServiceName = "orders";
    o.TraceSampleRatio = 0.1;
    o.ActivitySources.Add("MyApp.Orders");
    o.Meters.Add("MyApp.Orders");
});
```

| Option | Valeur par défaut | Signification |
| --- | --- | --- |
| `Endpoint` | `http://localhost:4317` | Adresse OTLP de Flare.Ingest. |
| `Protocol` | `Grpc` | `HttpProtobuf` pour les réseaux où gRPC est bloqué. Utilisez le port HTTP (`4318`) ; les chemins `/v1/traces`, `/v1/logs` et `/v1/metrics` sont ajoutés pour vous. |
| `ApiKey` | aucune | Envoyée en `Authorization: Bearer`. Voir [clés d'ingestion](#utiliser-une-clé-dingestion). |
| `ServiceName`, `ServiceVersion` | la ressource existante de l'application | Les attributs `service.name` et `service.version`. |
| `TraceSampleRatio` | `1` | Fraction des nouvelles traces à conserver, de 0 à 1. |
| `InstrumentAspNetCore`, `InstrumentHttpClient`, `InstrumentRuntime` | `true` | Désactive une instrumentation intégrée. |
| `ActivitySources`, `Meters` | vide | Noms de vos propres `ActivitySource` et `Meter` à exporter. |

Les mêmes paramètres se lient depuis la section de configuration `Flare` : un
déploiement peut donc les définir par variables d'environnement et le code reste
`builder.AddFlareTelemetry()` :

```bash
Flare__Endpoint=http://flare:4317
Flare__ApiKey=<clé d'ingestion>
Flare__TraceSampleRatio=0.1
```

Les valeurs définies dans le délégué remplacent celles de la section de configuration.

## Échantillonner les traces

Un `TraceSampleRatio` inférieur à `1` installe un échantillonneur à ratio basé sur
le parent. Le service qui démarre une trace décide de la conserver, et chaque
service qui la rejoint suit cette décision : une trace conservée n'a donc jamais de
spans manquants provenant d'un service en aval. Pour que cela tienne, donnez le même
assistant, ou un échantillonneur équivalent basé sur le parent, à chaque service du
chemin.

Il s'agit d'un échantillonnage en tête, dans l'application. Flare peut aussi
échantillonner à l'ingestion ; voir
[échantillonnage, clustering et coût](../../explanation/sampling-clustering-and-cost.fr.md)
si vous préférez que la décision soit prise de façon centralisée.

## Utiliser une clé d'ingestion

Si votre instance Flare
[exige des clés d'API d'ingestion](../admin/configure-authentication.fr.md#clés-api-dingestion),
passez la clé en second argument, ou définissez `Flare__ApiKey` :

```csharp
builder.AddFlareTelemetry("http://flare:4317", apiKey);
```

Gardez la clé hors du contrôle de source : lisez-la depuis la configuration ou un
coffre de secrets.

## L'utiliser avec d'autres exportateurs

L'assistant enregistre ses exportateurs sous leurs propres noms (`flare-logs`,
`flare-traces`, `flare-metrics`) : il s'ajoute à un exportateur que votre application
a déjà, par exemple un qui envoie vers un autre backend, au lieu de le remplacer. Ne
le mélangez pas avec `UseOtlpExporter()` (tous signaux) : le SDK OpenTelemetry refuse
de démarrer quand celui-ci et un exportateur propre à un signal sont tous deux
enregistrés.

## Dépannage

- **Rien n'arrive.** Vérifiez que le port correspond au protocole (`4317` gRPC,
  `4318` HTTP) et que l'application peut l'atteindre. Les métriques sont exportées
  toutes les 60 secondes par défaut : les premiers points peuvent mettre jusqu'à une
  minute à apparaître.
- **Rien n'arrive et un proxy devant Flare journalise `301` ou `308`.** OpenTelemetry
  .NET 1.20.1 et les versions suivantes ne suivent pas les redirections HTTP : un proxy
  qui redirige `http://` vers `https://` fait échouer chaque export. Pointez le point
  de terminaison vers l'adresse `https://` finale, ou faites servir le port OTLP par
  le proxy sans redirection.
- **`401` de Flare.** L'instance exige une clé d'ingestion ; voir
  [Utiliser une clé d'ingestion](#utiliser-une-clé-dingestion).
- **`ArgumentOutOfRangeException` au démarrage.** `TraceSampleRatio` doit être
  compris entre 0 et 1.
- **Un span ou un log affiche « Attributs ignorés : N ».** Le SDK OpenTelemetry de
  l'application a atteint une limite de nombre et a écarté N éléments avant l'envoi,
  donc Flare ne peut pas les afficher. Augmentez la limite correspondante dans
  l'application : `OTEL_SPAN_ATTRIBUTE_COUNT_LIMIT`, `OTEL_SPAN_EVENT_COUNT_LIMIT`,
  `OTEL_SPAN_LINK_COUNT_LIMIT` ou `OTEL_LOGRECORD_ATTRIBUTE_COUNT_LIMIT` (chacune
  retombe sur `OTEL_ATTRIBUTE_COUNT_LIMIT`, 128 par défaut). Ces compteurs nécessitent la
  [migration 0091](../../../db/clickhouse/0091_dropped_counts.sql) sur un ClickHouse
  existant ; les lignes stockées avant n'affichent rien.
