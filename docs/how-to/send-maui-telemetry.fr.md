# Comment envoyer la télémétrie d'une application .NET MAUI à Flare

Envoyez traces, logs, métriques et exceptions non gérées d'une application
.NET MAUI (Android, iOS, macOS, Windows) vers Flare avec les paquets
OpenTelemetry .NET standard. Les appels HTTP de l'application partagent
l'identifiant de trace de votre backend : un écran lent peut être suivi jusque
dans l'appel d'API et ses requêtes de base de données.

Il n'existe pas de paquet MAUI propre à Flare. Le récepteur OTLP/HTTP de Flare
accepte directement les exports protobuf du SDK, et un téléphone impose
quelques contraintes qu'un serveur n'a pas (voir [Limites](#limites)).

## Prérequis

- Une instance Flare avec le port OTLP/HTTP (`4318`) joignable depuis les
  appareils de vos utilisateurs, idéalement en HTTPS.
- Une application MAUI à laquelle vous pouvez ajouter des paquets NuGet.

## Utiliser le paquet Flare.Maui

`Flare.Maui` réalise en un appel la configuration ci-dessous : export OTLP/HTTP, file de reprise hors ligne, attributs d'appareil et de session, spans `HttpClient` et de navigation Shell, capture des exceptions non gérées et vidage du tampon au passage en arrière-plan. Il n'est pas encore sur NuGet ; pour l'instant, référencez `src/Flare.Maui` de ce dépôt.

```csharp
builder.UseMauiApp<App>()
       .UseFlare(o =>
       {
           o.Endpoint = new Uri("https://flare.example.com:4318");
           o.ServiceName = "my-maui-app";
           o.IngestKey = "<clé limitée à my-maui-app>";
       });
```

Ajoutez vos propres noms d'`ActivitySource` et de `Meter` à `o.AdditionalSources` et `o.AdditionalMeters`, et appelez `FlareMaui.RecordException(ex)` pour les exceptions que vous gérez. Les rapports de plantage natifs et Windows ne sont pas encore couverts. Le reste de cette page montre la configuration OpenTelemetry équivalente, câblée à la main, utile seulement si vous voulez un contrôle total ; les sections sur les clés d'ingestion, le développement local et les limites s'appliquent aux deux.

## Utiliser OTLP/HTTP, pas gRPC

gRPC est peu fiable sur iOS et Android : exportez avec le protocole
`HttpProtobuf`. Quand vous définissez le endpoint dans le code, incluez le
chemin du signal (`/v1/traces`, `/v1/logs`, `/v1/metrics`).

## Installer les paquets

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.Http
```

## Configurer le SDK

MAUI n'a pas d'hôte générique : le service hébergé dont dépend
`AddOpenTelemetry()` ne démarre jamais. Construisez les fournisseurs
directement et conservez une référence pendant toute la vie de l'application.
Placez ceci dans `MauiProgram.cs` :

```csharp
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class MauiProgram
{
    const string FlareUrl = "https://flare.example.com:4318";
    const string IngestKey = "<ingest key>"; // ships in the binary: see "Ingest keys"

    // Held for the app's lifetime; disposing them flushes and stops export.
    public static TracerProvider? Tracing;
    public static MeterProvider? Metering;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var resource = ResourceBuilder.CreateDefault()
            .AddService("my-maui-app", serviceVersion: AppInfo.Current.VersionString)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("os.type", DeviceInfo.Platform.ToString().ToLowerInvariant()),
                new("os.version", DeviceInfo.VersionString),
                new("device.manufacturer", DeviceInfo.Manufacturer),
                new("device.model.identifier", DeviceInfo.Model),
                new("app.build", AppInfo.Current.BuildString),
            });

        void Configure(OtlpExporterOptions o, string path)
        {
            o.Endpoint = new Uri($"{FlareUrl}{path}");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Headers = $"Authorization=Bearer {IngestKey}";
        }

        Tracing = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource("MyApp")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => Configure(o, "/v1/traces"))
            .Build();

        Metering = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("MyApp")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => Configure(o, "/v1/metrics"))
            .Build();

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = true;
            o.AddOtlpExporter(e => Configure(e, "/v1/logs"));
        });

        return builder.Build();
    }
}
```

Créez vos propres spans depuis une `ActivitySource` nommée `MyApp` :

```csharp
static readonly ActivitySource Source = new("MyApp");

using var activity = Source.StartActivity("OpenOrder");
activity?.SetTag("order.id", id);
```

`AddHttpClientInstrumentation()` trace les appels `HttpClient` et envoie
l'en-tête `traceparent` à votre API, de sorte que la trace se poursuit côté
serveur.

## Ajouter un identifiant de session

Regroupez la télémétrie d'un lancement avec l'attribut `session.id`. Générez-en
un par lancement et apposez-le sur chaque span avec un petit processeur :

```csharp
sealed class SessionProcessor : BaseProcessor<Activity>
{
    static readonly string SessionId = Guid.NewGuid().ToString("N");
    public override void OnStart(Activity activity) => activity.SetTag("session.id", SessionId);
}
```

Enregistrez-le avec `.AddProcessor(new SessionProcessor())` avant l'exportateur.
Filtrez sur `session.id` dans **Traces** pour voir un lancement de bout en bout.

La page **Sessions** (menu `⋯`) liste chaque lancement de la fenêtre avec la version de
l'application, l'appareil, les écrans, le nombre de traces et d'erreurs. Filtrez par version
ou ne gardez que les sessions en erreur, puis cliquez sur une session pour ouvrir ses traces.
Le paquet définit `session.id` pour vous ; une configuration manuelle a besoin du processeur ci-dessus.

## Signaler les exceptions non gérées

La page **Errors** de Flare regroupe les exceptions enregistrées sur des spans.
Signalez chaque plantage comme un span court avec un événement d'exception, puis
videz le tampon avant la fin du processus :

```csharp
static readonly ActivitySource Errors = new("MyApp");

static void Report(Exception ex, bool fatal)
{
    using var span = Errors.StartActivity("app.unhandled_exception");
    span?.AddException(ex);
    span?.SetStatus(ActivityStatusCode.Error, ex.Message);
    span?.SetTag("exception.escaped", fatal);
    span?.Stop();
    if (fatal) MauiProgram.Tracing?.ForceFlush(2000);
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Report((Exception)e.ExceptionObject, fatal: true);
TaskScheduler.UnobservedTaskException += (_, e) => Report(e.Exception, fatal: false);
```

Installez les gestionnaires dans `CreateMauiApp`, une fois les fournisseurs
construits. Les erreurs sont regroupées par type et message d'exception. Ajoutez
le filtre de ressource `os.type = android` (ou `ios`) sur la page **Errors**
pour voir une seule plateforme. Les plantages natifs fatals (un SIGSEGV, un kill
par le watchdog iOS) n'atteignent aucun gestionnaire managé et ne sont pas
signalés.

## Symboliser les stack traces des builds release

Les builds release sont trimmed, souvent compilés en AOT, et sans PDB : un plantage s'affiche `at MyApp.Cart.Add (System.String sku) [0x0001a] in <8e3f…>:0`. Envoyez les symboles de chaque release depuis la CI, avec la valeur `--release` que l'application rapporte comme `service.version` :

```bash
flare sourcemaps upload-dotnet obj/Release/net10.0-android \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

La commande lit chaque dll et son PDB portable (`<DebugType>portable</DebugType>`, valeur par défaut) ; la page **Errors** affiche alors `File.cs:line 23` pour les frames résolues.

Pour un build **Native AOT** (`PublishAot`, iOS), les frames s'affichent `at MyApp.Cart.Add(String) + 0x48`. Envoyez le `.dSYM` que `dotnet publish` écrit à côté du binaire :

```bash
flare sourcemaps upload-native bin/Release/net10.0-ios/ios-arm64/publish/MyApp.dSYM \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

Les méthodes surchargées restent non résolues : la numérotation des surcharges par le compilateur ne peut pas être retrouvée à partir des symboles.

## Vider le tampon quand l'application passe en arrière-plan

Les OS mobiles suspendent ou tuent une application en arrière-plan sans
préavis. Videz le tampon depuis l'événement `Stopped` de la fenêtre pour que le
dernier lot quitte l'appareil :

```csharp
protected override Window CreateWindow(IActivationState? state)
{
    var window = base.CreateWindow(state);
    window.Stopped += (_, _) =>
    {
        MauiProgram.Tracing?.ForceFlush(2000);
        MauiProgram.Metering?.ForceFlush(2000);
    };
    return window;
}
```

## Clés d'ingestion

Si les [clés API d'ingestion](configure-authentication.fr.md#clés-api-dingestion)
sont requises, l'exportateur a besoin de l'en-tête `Authorization` ci-dessus. La
clé est compilée dans votre application : quiconque décompresse le binaire peut
la lire. Considérez-la comme publique :

- Créez une clé dédiée à l'application, pas une clé partagée avec vos serveurs.
- Limitez-la au nom de service de l'application pour qu'elle ne puisse écrire
  sous aucun autre service : `flare apikey scope` ou
  `PUT /api/ingest-keys/{id}/services` avec `{ "services": ["my-maui-app"] }`.
  Un export contenant un autre `service.name` (ou aucun) est refusé avec `403`.
- Définissez des [limites par clé](configure-authentication.fr.md#clés-api-dingestion)
  pour plafonner ce qu'une clé divulguée peut envoyer. Une clé au plafond reçoit
  `429` avec `Retry-After`.
- Ne définissez **pas** d'origines autorisées. Une clé restreinte par origine ne
  fonctionne que depuis une requête de navigateur dont l'en-tête `Origin`
  correspond ; une application native n'en envoie pas et serait refusée. Les
  origines et `Otlp__AllowedOrigins` concernent les
  [applications navigateur](send-browser-telemetry.fr.md#clés-dingestion).
- Faites tourner la clé en publiant une nouvelle version de l'application, puis
  révoquez l'ancienne clé une fois son trafic écoulé.

## Tester contre un Flare local

Un émulateur Android joint votre ordinateur à `10.0.2.2`, pas à `localhost` :
utilisez `http://10.0.2.2:4318`. Le simulateur iOS et Windows utilisent
`localhost`. Le `http://` en clair est bloqué par défaut sur les deux
plateformes mobiles ; en développement uniquement, autorisez le trafic en clair
sur Android (`android:usesCleartextTraffic="true"` sur l'élément
`<application>`) et ajoutez une exception App Transport Security pour votre hôte
sur iOS. Utilisez HTTPS pour tout ce que vous publiez.

## Vérifier que ça fonctionne

Ouvrez la page **Traces** et filtrez sur le service `my-maui-app`. Un appel
`HttpClient` apparaît comme un span client, et comme une seule trace couvrant
les deux services lorsque votre API propage `traceparent`. Ouvrez **Logs** et
filtrez sur le même service pour voir la sortie `ILogger`. Levez une exception
depuis un gestionnaire de bouton pour confirmer qu'elle arrive dans **Errors**.

## Limites

- Rien n'est mis en file sur disque. Un lot dont l'envoi échoue (pas de réseau,
  mode avion) est réessayé brièvement puis abandonné, et tout ce qui est en
  tampon quand l'OS tue l'application est perdu.
- Les plantages natifs et les traces de pile des builds réduits (trimming) ou AOT
  ne sont pas symbolisés : les frames affichent des noms de méthodes du runtime
  sans lignes de source.
- Le SDK OpenTelemetry n'est pas annoté de bout en bout pour le trimming/AOT.
  Testez un build release avec le trimming activé avant de vous y fier.
- La télémétrie transporte ce que vous y attachez. Ne mettez ni noms, ni e-mails,
  ni autres données personnelles dans les attributs, et traitez tout identifiant
  d'appareil stable comme facultatif (opt-in).
