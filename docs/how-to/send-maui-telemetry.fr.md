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

Ajoutez vos propres noms d'`ActivitySource` et de `Meter` à `o.AdditionalSources` et `o.AdditionalMeters`, et appelez `FlareMaui.RecordException(ex)` pour les exceptions que vous gérez. Les plantages natifs sont signalés au lancement suivant ([détails](#signaler-les-plantages-natifs)) ; Windows n'est pas encore couvert. Le reste de cette page montre la configuration OpenTelemetry équivalente, câblée à la main, utile seulement si vous voulez un contrôle total ; les sections sur les clés d'ingestion, le développement local et les limites s'appliquent aux deux.

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
Le paquet définit `session.id` pour vous, sur les spans et sur les enregistrements `ILogger` (filtrez **Logs** sur `session.id`) ; une configuration manuelle a besoin du processeur ci-dessus.

## Laisser des fils d'Ariane (breadcrumbs)

Le paquet enregistre un span `breadcrumb` pour chaque passage au premier plan ou en arrière-plan, appui sur un
bouton, affichage de page et enregistrement `ILogger` de niveau Information ou plus. Ils apparaissent sur la page
de session à côté des spans : un plantage arrive avec ce que l'utilisateur faisait juste avant. Ajoutez les vôtres
avec `FlareMaui.AddBreadcrumb("checkout", "payment sheet opened")`.

Le texte des boutons et les titres de page peuvent contenir des données personnelles ; ils sont omis sauf si vous
activez `IncludeTextInBreadcrumbs` ou `IncludeTitleInBreadcrumbs`. Relevez `BreadcrumbLogLevel` (ou `None`) pour
limiter les breadcrumbs de logs, et `Breadcrumbs = false` pour tout désactiver.

## Attacher un utilisateur, des tags et masquer des données

```csharp
FlareMaui.SetUser("account-42");
FlareMaui.SetTag("plan", "pro");
FlareMaui.SetContext("cart", new Dictionary<string, string> { ["items"] = "3" });   // cart.items
```

Seul l'identifiant utilisateur est envoyé. Un nom ou un e-mail passé à `SetUser` est ignoré sauf si vous définissez
`SendDefaultPii = true`. Pour masquer ou supprimer autre chose avant que cela quitte l'appareil, définissez
`ScrubAttribute` : il reçoit chaque tag de span et chaque attribut de log et renvoie la valeur à envoyer, ou `null`
pour la supprimer :

```csharp
o.ScrubAttribute = (key, value) => key == "http.url" ? Redact((string?)value) : value;
```

Aucun identifiant d'appareil n'est envoyé dans les deux cas.

Pour supprimer entièrement un span, définissez `BeforeSend`, qui renvoie `false` pour les spans à ne pas envoyer
(sans effet sur les logs). `ScrubAttribute` reçoit aussi les noms de span (clé `span.name`), les messages de statut
(`status.message`) et, pour les exceptions signalées par Flare lui-même, `exception.type`, `exception.message` et
`exception.stacktrace`. Les exceptions enregistrées par d'autres instrumentations ne sont pas nettoyées.

Les exports échoués sont rejoués depuis le disque. Au démarrage, Flare supprime les fichiers en attente plus
vieux que `OfflineQueueMaxAge` (2 jours), puis les plus anciens jusqu'à passer sous `OfflineQueueMaxBytes` (25 Mo).
C'est un nettoyage au démarrage : la file peut dépasser cette taille pendant l'exécution.

## Vérifier la santé d'une version

Le haut de la page **Sessions** affiche, pour chaque version de l'application, les sessions sans plantage et les
utilisateurs sans plantage sur la fenêtre, afin de comparer une nouvelle version à la précédente. Une session compte
comme plantée quand l'application a signalé une exception fatale non gérée ; le taux est en rouge sous 99 %. Les
utilisateurs sont comptés à partir d'un attribut `user.id` sur les spans ou la ressource. `Flare.Maui` n'en définit
pas : les colonnes d'utilisateurs affichent un tiret tant que votre application n'en ajoute pas.

## Détecter les blocages de l'application

Un chien de garde envoie un ping au thread d'interface et, s'il ne répond pas pendant `AppHangThreshold` (2 secondes
par défaut, 500 ms minimum), signale un span `app.hang` avec un statut d'erreur. Il apparaît sur la page de session
avec un breadcrumb `hang`, donc un écran figé reste visible même si l'OS tue ensuite l'application. Il est en pause
quand l'application est en arrière-plan. Sur Android, le span porte aussi `hang.stacktrace`, la pile Java du thread bloqué (les frames gérées apparaissent
comme celles natives du runtime) ; iOS ne permet pas de lire la pile d'un autre thread. Un débogueur en pause
ressemble à un blocage : mettez `DetectAppHangs = false` pendant le débogage si cela gêne.

## Joindre des captures d'écran aux erreurs

Activez `CaptureScreenshotOnError = true` pour envoyer une capture de la page courante à chaque exception signalée.
Elle apparaît sous la forme d'un bouton **Screenshot** sur la ligne de la page de session. C'est désactivé par défaut,
car une capture montre tout ce qui est à l'écran, y compris le texte saisi. Les images sont des JPEG, maintenues sous
`ScreenshotMaxBytes` (300 Ko par défaut, 512 Ko au plus) en baissant la qualité, et limitées à 5 par lancement et une
par 10 secondes. L'envoi va vers `POST /v1/screenshots`, sur le même point d'accès et avec la même clé d'ingestion, et
suit le réglage de rétention des traces. Masquez les vues sensibles dans votre application avant qu'elles puissent être capturées.

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
par le watchdog iOS) n'atteignent aucun gestionnaire managé. Le paquet `Flare.Maui` les signale
au lancement suivant, voir [Signaler les plantages natifs](#signaler-les-plantages-natifs) ; une configuration manuelle ne le fait pas.

## Signaler les plantages natifs

Certains plantages terminent le processus avant qu'un gestionnaire managé ne s'exécute : un SIGSEGV, un ANR ou un kill pour
manque de mémoire sous Android, une terminaison par le watchdog iOS. Au lancement suivant, `Flare.Maui` lit ce que le système
a enregistré sur le lancement précédent et envoie chaque cas comme un span `app.unhandled_exception` avec
`exception.escaped = true`. Le span porte l'ID de session et la version de l'application de ce lancement : la santé des
versions le compte donc comme un plantage de cette version, et la page Errors le regroupe sous un type `Native.*`.

- **Android 11+** lit `ApplicationExitInfo` : plantages natifs, ANR (avec le texte de la trace), échecs d'initialisation,
  usage excessif de ressources et kills pour manque de mémoire pendant que l'application était visible.
- **iOS 14+ et Mac Catalyst** lisent les diagnostics de plantage MetricKit. iOS les livre jusqu'à un jour plus tard : l'heure
  est donc estimée d'après le dernier passage au premier plan ou en arrière-plan, et la pile est le JSON brut de
  MetricKit, non symbolisé.

Un plantage dont l'exception managée fatale a déjà été signalée n'est pas compté deux fois. Le paquet garde un petit
journal des dix derniers lancements (ID de session, version, application au premier plan ou non) dans `flare/runs.json`
du dossier de données de l'application ; aucun identifiant d'appareil n'est stocké. Désactivez avec
`o.CaptureNativeCrashes = false`. Pas encore vérifié sur un appareil.

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
  --managed obj/Release/net10.0-ios/ios-arm64 \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

`--managed` prend la dll et le PDB portable à partir desquels l'application a été compilée (un fichier ou un répertoire, répétable ; utilisez ceux de `obj/`, pas une copie d'un autre build). Sans cette option, une méthode surchargée reste non résolue : le compilateur numérote les surcharges `Add`, `Add_0`, `Add_1` et le `.dSYM` seul ne dit pas laquelle est laquelle. Avec elle, Flare relit le même ordre dans les métadonnées de la dll et compare les types de paramètres de la frame (`Add(String)`, `Add(Int32)`). Une fonction dont les lignes ne correspondent pas au PDB reste non associée, avec une note, de sorte qu'une dll obsolète ne donne jamais une mauvaise ligne.

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
