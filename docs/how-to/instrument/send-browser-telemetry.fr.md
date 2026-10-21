# Comment envoyer la télémétrie du navigateur à Flare

Envoyez les chargements de page, les spans `fetch` et les erreurs JavaScript d'une
application web vers Flare avec le SDK OpenTelemetry JavaScript. Les spans du
navigateur partagent l'identifiant de trace de votre backend .NET : un clic lent
peut donc être suivi jusqu'à l'appel d'API et ses requêtes de base de données.

Le récepteur OTLP/HTTP de Flare accepte directement les exports JSON du SDK. La
seule étape supplémentaire est de lui indiquer quelles origines peuvent lui
envoyer des données.

## Prérequis

- Une instance Flare en cours d'exécution, dont le port OTLP/HTTP (`4318`) est
  joignable depuis les navigateurs de vos utilisateurs.
- Une application web à laquelle vous pouvez ajouter des paquets npm.

## Autoriser l'origine de votre site

Les navigateurs refusent les requêtes cross-origin tant que le récepteur ne
répond pas à une requête préliminaire CORS. Par défaut, Flare n'envoie aucun
en-tête CORS. Listez l'origine de votre site dans Flare.Ingest :

```bash
Otlp__AllowedOrigins__0=https://app.example.com
Otlp__AllowedOrigins__1=http://localhost:5173
```

Redémarrez Flare.Ingest. Seul `POST` est autorisé, avec les en-têtes
`Content-Type`, `Content-Encoding` et `Authorization`. `*` autorise toute origine.

## Installer et configurer le SDK

```bash
npm install @opentelemetry/api @opentelemetry/sdk-trace-web \
  @opentelemetry/exporter-trace-otlp-http @opentelemetry/resources \
  @opentelemetry/instrumentation-document-load \
  @opentelemetry/instrumentation-fetch
```

```ts
import { WebTracerProvider, BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';

const provider = new WebTracerProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  spanProcessors: [
    new BatchSpanProcessor(
      new OTLPTraceExporter({ url: 'https://flare.example.com:4318/v1/traces' })
    )
  ]
});
provider.register();

registerInstrumentations({
  instrumentations: [
    new DocumentLoadInstrumentation(),
    new FetchInstrumentation({
      // Send the traceparent header to your own API so the trace continues server side.
      propagateTraceHeaderCorsUrls: [/api\.example\.com/]
    })
  ]
});
```

Votre API doit aussi autoriser l'en-tête `traceparent` dans sa propre
politique CORS, sinon le navigateur bloque l'appel.

## Clés d'ingestion

Si les [clés d'API d'ingestion](../admin/configure-authentication.fr.md#clés-api-dingestion)
sont obligatoires, l'exporteur a besoin de
`headers: { Authorization: 'Bearer <clé>' }`. Cette clé est livrée dans votre
JavaScript : tous les visiteurs peuvent la lire. Créez une clé dédiée au
navigateur et limitez-la aux origines de votre site :

Dans le tableau de bord, ouvrez **Settings > Ingest keys** et utilisez le bouton globe d'une clé pour modifier ses origines et services autorisés ; `flare apikey scope` fait de même depuis un terminal. Les appels d'API correspondants :

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/origins \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "origins": ["https://app.example.com"] }'
```

Une clé restreinte n'est acceptée que pour une requête de navigateur dont l'en-tête
`Origin` figure dans la liste ; curl et les exporteurs serveur reçoivent `403`. Une
origine listée est aussi autorisée par CORS : avec une clé restreinte,
`Otlp__AllowedOrigins` n'est pas nécessaire. Les changements s'appliquent sous 30
secondes. Une liste vide supprime la restriction.

Vous pouvez aussi limiter la clé aux services pour lesquels elle peut écrire. Un export
contenant un autre `service.name` (ou aucun) est refusé avec `403` :

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/services \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "services": ["my-web-app"] }'
```

Cela ne rend pas la clé secrète : n'importe qui peut forger un en-tête `Origin`
hors d'un navigateur. Définissez des
[limites par clé](../admin/configure-authentication.fr.md#clés-api-dingestion) pour plafonner
ce qu'une clé divulguée peut envoyer.

## Signaler les erreurs JavaScript

La page **Errors** de Flare regroupe les exceptions enregistrées sur des spans. Signalez chaque erreur non interceptée sous forme de court span avec un événement d'exception :

```ts
import { trace, SpanStatusCode } from '@opentelemetry/api';

const tracer = trace.getTracer('browser-errors');

function report(error: unknown) {
  const err = error instanceof Error ? error : new Error(String(error));
  const span = tracer.startSpan('js.error');
  span.recordException(err);
  span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
  span.end();
}

window.addEventListener('error', (e) => report(e.error ?? e.message));
window.addEventListener('unhandledrejection', (e) => report(e.reason));
```

Les erreurs apparaissent sur **Errors**, regroupées par type et message. Ajoutez le filtre de ressource `telemetry.sdk.language = webjs` pour ne voir que les erreurs du navigateur. Levez des objets `Error` : une chaîne levée n'a pas de type et n'est donc pas regroupée. Les traces de pile des bundles minifiés ne sont pas encore symbolisées.

## Signaler les web vitals

Enregistrez chaque Core Web Vital sous forme d'histogramme nommé `browser.web_vital.<name>` (`lcp`, `inp`, `cls`, `fcp`, `ttfb`) avec le `rating` (`good`, `needs-improvement`, `poor`) en attribut :

```bash
npm install web-vitals @opentelemetry/sdk-metrics \
  @opentelemetry/exporter-metrics-otlp-http
```

```ts
import { metrics } from '@opentelemetry/api';
import { MeterProvider, PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { onCLS, onFCP, onINP, onLCP, onTTFB } from 'web-vitals';

const meterProvider = new MeterProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  readers: [
    new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter({ url: 'https://flare.example.com:4318/v1/metrics' }),
      exportIntervalMillis: 30_000
    })
  ]
});
metrics.setGlobalMeterProvider(meterProvider);
const meter = metrics.getMeter('web-vitals');

const MS = [100, 200, 500, 1000, 2000, 2500, 4000, 6000, 10000];
const SCORE = [0.01, 0.05, 0.1, 0.15, 0.25, 0.5, 1];

function track(name: string, unit: string, buckets: number[], subscribe: typeof onLCP) {
  const histogram = meter.createHistogram(`browser.web_vital.${name}`, {
    unit,
    advice: { explicitBucketBoundaries: buckets }
  });
  subscribe(({ value, rating }) => histogram.record(value, { rating }));
}

track('lcp', 'ms', MS, onLCP);
track('inp', 'ms', MS, onINP);
track('fcp', 'ms', MS, onFCP);
track('ttfb', 'ms', MS, onTTFB);
track('cls', '1', SCORE, onCLS);

// INP and CLS settle when the page is hidden; flush before the tab goes away.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'hidden') void meterProvider.forceFlush();
});
```

Sur la page **Dashboards**, choisissez **Create from template > Web vitals**. Les panneaux affichent les percentiles de chaque indicateur ; les objectifs de Google se mesurent au p75 (LCP sous 2,5 s, INP sous 200 ms, CLS sous 0,1).

## Ventiler les vitals par route

Ajoutez `url.template` (le motif de route comme `/orders/:id`, pas l'URL brute) aux attributs passés à `histogram.record`. Déplier un service sur la page **Frontend** affiche alors les vitals de chaque route. La colonne **Chargements de page** compte les mesures FCP, une par chargement.

## Symboliser les traces de pile minifiées

Les bundles de production sont minifiés : la pile d'une erreur ressemble à `at o (…/app-abc123.js:1:30)`. Importez les source maps du build et Flare affiche à la place la fonction, le fichier, la ligne et la colonne d'origine sur **Errors** ; une occurrence dont la trace a été réécrite porte le badge **Source map applied**.

Faites d'abord déclarer à l'application la version dont elle provient, avec la même valeur que lors de l'import :

```ts
resourceFromAttributes({ 'service.name': 'my-web-app', 'service.version': '1.4.2' })
```

Importez ensuite les maps depuis la sortie du build dans une étape CI, avec un [jeton d'accès personnel](../admin/configure-authentication.md) d'administrateur dans `FLARE_API_TOKEN` :

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

Chaque fichier `dist/**/*.map` est stocké sous son chemin sans `.map` : `dist/assets/app-abc123.js.map` devient `assets/app-abc123.js`. Une frame correspond au plus long chemin stocké par lequel se termine l'URL de son script. `flare sourcemaps list` montre ce qui est stocké et `flare sourcemaps delete --service my-web-app --release 1.4.2` supprime une version. L'API brute est `PUT /api/source-maps?service=&version=&bundle=` avec le JSON de la map comme corps.

La symbolisation a lieu à l'ouverture d'une occurrence : les maps importées après une erreur s'y appliquent aussi. Une map est associée via `service.name` et `service.version` (ou l'attribut de ressource `vcs.revision`) ; conservez donc les maps des anciennes versions tant que leurs erreurs surviennent. Les maps jusqu'à 50 Mo sont acceptées ; les index maps (`sections`) sont refusées. Ne servez pas les fichiers `.map` publiquement si vous ne voulez pas exposer votre source, et ne les déployez pas avec le bundle.

## Voir les données sur la page Frontend

Ouvrez **Frontend** (dans la liste **Plus** du menu utilisateur). Elle affiche, par service, le 75e centile de LCP, INP, CLS, FCP et TTFB sur la période choisie, coloré selon les seuils de Google, avec la part des mesures jugées bonnes par le navigateur, ainsi que les principales erreurs JavaScript du navigateur. Ce sont les exceptions des ressources avec `telemetry.sdk.language = webjs` ; **Ouvrir dans Erreurs** y mène. **Paramètres > Source maps** liste les maps envoyées par version et permet de les supprimer.

## Enregistrer le replay de session

Flare peut rejouer ce qu'un utilisateur a vu dans le navigateur : chargements de page, clics, défilement et texte affiché,
lus à côté des spans et des erreurs de la session. L'enregistreur est `@apardev/flare-browser-replay`, une fine couche
au-dessus de [rrweb](https://github.com/rrweb-io/rrweb) qui envoie l'enregistrement à `POST /v1/replay` de Flare.Ingest.
Il est facultatif et n'enregistre rien tant que vous ne le démarrez pas.

```bash
npm install @apardev/flare-browser-replay
```

```ts
import { startReplay } from '@apardev/flare-browser-replay';

const replay = startReplay({
  endpoint: 'https://flare.example.com:4318',
  service: 'my-web-app',
  ingestKey: '<une clé d'ingestion limitée au navigateur>',
  sampleRate: 0.25 // facultatif : enregistrer une session sur quatre
});
```

L'origine doit être autorisée comme pour OTLP (voir [Autoriser l'origine de votre site](#autoriser-lorigine-de-votre-site)) ;
la même clé d'ingestion convient, et une clé limitée à des services doit lister `my-web-app`. Un ClickHouse existant
nécessite la [migration 0090](../../../db/clickhouse/0090_web_replay_chunks.sql).

### Relier l'enregistrement aux spans de la session

Un enregistrement est stocké sous un identifiant de session, et Flare l'affiche sur la page de cette session. Les spans
apparaissent sur la même page lorsqu'ils portent cet identifiant dans l'attribut `session.id`. `replay.sessionId` est
l'identifiant de cet onglet : copiez-le sur vos spans.

```ts
import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';

class SessionIdProcessor implements SpanProcessor {
  onStart(span: Span, _context: Context) { span.setAttribute('session.id', replay.sessionId); }
  onEnd(_span: ReadableSpan) {}
  forceFlush() { return Promise.resolve(); }
  shutdown() { return Promise.resolve(); }
}
// à ajouter aux spanProcessors du WebTracerProvider, avant le BatchSpanProcessor
```

Ouvrez ensuite **Sessions**, choisissez la session et cliquez sur **Voir l'enregistrement**. **Afficher dans le replay**
sur une ligne de span place le lecteur au début de ce span. Une session qui a un enregistrement mais aucun span reste
accessible à `/sessions/<id>`.

### Ce qui est masqué

Tout ce qui est saisi dans un champ de formulaire est remplacé par des astérisques, et un champ de mot de passe l'est
toujours. Pour le reste :

| Pour | Faire |
| --- | --- |
| Exclure entièrement un élément (il est rejoué comme une zone vide) | ajouter l'attribut `data-flare-block` ou la classe `flare-block` |
| Masquer le texte d'un élément | ajouter `data-flare-mask` ou la classe `flare-mask` |
| Masquer tout le texte de la page | `startReplay({ ..., maskAllText: true })` |
| Afficher les champs tels que saisis | `startReplay({ ..., maskAllInputs: false })` (déconseillé) |

Le masquage se fait dans le navigateur avant tout envoi ; il vaut donc ce que vaut le balisage. Flare ne peut pas savoir ce
que contient un événement enregistré, et quiconque détient une clé d'ingestion peut envoyer des événements quelconques.
Examinez quelques enregistrements avant d'activer le replay pour tous les utilisateurs, et marquez tout ce qui est personnel
sans être un champ de formulaire (page de compte, adresse affichée en texte). Les feuilles de style sont intégrées à
l'enregistrement, mais les images et les polices sont chargées depuis votre site au moment du visionnage : le navigateur du
lecteur doit pouvoir l'atteindre.

### Options

| Option | Par défaut | Signification |
| --- | --- | --- |
| `sessionId` | un par onglet, conservé dans `sessionStorage` | L'identifiant sous lequel l'enregistrement est stocké. À fournir pour réutiliser un identifiant existant. |
| `sampleRate` | `1` | Part des sessions à enregistrer, décidée une fois par session. |
| `flushIntervalMs` | `5000` | Fréquence d'envoi d'un lot. |
| `maxEventsPerChunk` | `500` | Un lot part plus tôt à ce nombre d'événements. |
| `maxDurationMs` | une heure | L'enregistrement s'arrête après cette durée ; le tableau de bord liste une heure. |
| `checkoutEveryMs` | désactivé | Prendre un nouvel instantané de la page à cet intervalle, pour qu'un replay puisse démarrer plus tard dans une longue session. |
| `mode` | `'session'` | `'session'` enregistre tout ; `'onError'` n'enregistre qu'autour des erreurs (voir ci-dessous). |
| `bufferMs` | `30000` | `onError` seulement : jusqu'où l'enregistrement d'une erreur remonte dans le temps. |
| `afterErrorMs` | `10000` | `onError` seulement : durée pendant laquelle l'enregistrement continue après une erreur. |
| `captureErrors` | `true` | `onError` seulement : traiter les erreurs non interceptées et les rejets de promesse non gérés comme des erreurs à conserver. |
| `captureConsole` | désactivé | Enregistrer la sortie console : `true` pour `warn` et `error`, ou une liste de niveaux (`'log'`, `'info'`, `'warn'`, `'error'`, `'debug'`). |
| `captureNetwork` | `false` | Enregistrer les requêtes `fetch`, `XMLHttpRequest` et beacon (URL, statut, durée). |
| `networkKeepQuery` | `false` | Conserver la chaîne de requête et le fragment des URL de requêtes enregistrées. |
| `captureCanvas` | désactivé | Enregistrer ce qu'affichent les éléments `<canvas>` : `true` pour 1 image par seconde, ou un nombre de 1 à 10. |
| `blockSelector`, `maskTextSelector` | `[data-flare-block]`, `[data-flare-mask]` | Sélecteurs des deux attributs ci-dessus. |
| `onStop` | aucun | Appelé avec une raison quand l'enregistreur s'arrête de lui-même. |

`replay.stop()` termine l'enregistrement et envoie le reste ; `replay.flush()` envoie ce qui a été enregistré jusque-là.

### N'enregistrer qu'autour d'une erreur

Enregistrer chaque session est ce qui coûte le plus à stocker et à examiner. Avec `mode: 'onError'`, l'enregistreur ne garde
en mémoire que les dernières secondes et n'envoie rien tant que rien ne va mal :

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, mode: 'onError', bufferMs: 30_000, afterErrorMs: 10_000 });

try { await checkout(); } catch (err) { report(err); replay.keep(); }   // une erreur que vous avez gérée vous-même
```

Une erreur non interceptée ou un rejet de promesse non géré fait la même chose sans code (désactivez-le avec
`captureErrors: false`). Lors d'une erreur, l'enregistreur envoie ce qu'il détient (entre `bufferMs` et deux fois `bufferMs`
avant l'erreur, en commençant à un instantané de la page) et continue d'enregistrer pendant `afterErrorMs`. Ensuite il
ignore les événements jusqu'au prochain instantané : une session avec plusieurs erreurs donne plusieurs enregistrements, et
une session sans erreur ne laisse rien. Rien ne quitte le navigateur avant l'erreur, mais la page est enregistrée en mémoire
en permanence ; les règles de masquage ci-dessus s'appliquent donc dans les deux cas.

### Afficher la console et les requêtes

La relecture peut lister ce que la page a journalisé et les requêtes qu'elle a faites, à côté de la vidéo. Les deux sont
désactivés par défaut, car ni l'un ni l'autre n'est masqué comme les champs de formulaire :

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, captureConsole: ['warn', 'error'], captureNetwork: true });
```

- **Console** : les niveaux choisis, plus les erreurs non interceptées et les rejets non gérés comme entrées `error`. Chaque
  argument est tronqué à 500 caractères et 3 niveaux de profondeur. Tout ce que la page journalise est stocké tel quel : ne
  journalisez pas ce que vous ne voudriez pas conserver.
- **Réseau** : les requêtes `fetch`, `XMLHttpRequest` et beacon, sous forme d'URL, de statut et de durée. Les en-têtes et les
  corps ne sont jamais enregistrés. La chaîne de requête et le fragment sont retirés de l'URL sauf si `networkKeepQuery` est
  activé, et les envois de l'enregistreur lui-même sont exclus. La méthode n'est pas enregistrée, et un navigateur qui ne
  fournit pas le statut de la réponse n'en affiche aucun.

Sous le lecteur, **Console** et **Réseau** listent les entrées. Une ligne ouvre l'enregistrement à cet instant, et les lignes
postérieures à la position courante sont atténuées.

### Afficher le contenu des canvas

Un `<canvas>` (graphique, carte, dessin, jeu) ne fait pas partie du balisage de la page : dans un enregistrement ordinaire il
apparaît vide. Activez `captureCanvas` pour enregistrer ce qu'il dessine :

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, captureCanvas: 2 });   // 2 images par seconde
```

- Désactivé par défaut. Les pixels ne se masquent pas comme le texte : un canvas qui dessine des données personnelles est
  stocké tel que dessiné. Un canvas situé dans un élément correspondant à `blockSelector` ou `maskTextSelector` n'est jamais
  enregistré, et `maskAllText: true` désactive la capture des canvas.
- Un canvas inchangé n'envoie rien. Chaque image est réduite à 800 pixels au plus, compressée en WebP (en PNG si le navigateur
  ne sait pas), et abandonnée si elle dépasse encore 150 Ko. Une session cesse de capturer les canvas après environ 25 Mo d'images.
- Les canvas 2D et WebGL fonctionnent. Un canvas ayant dessiné une image d'une autre origine sans CORS ne peut pas être lu par
  le navigateur et reste vide.
- À la lecture, les images sont peintes sur le canvas au fil de l'enregistrement, et une recherche affiche la dernière image
  précédente. Un canvas qui change plus vite que la cadence ressemble à un diaporama.

### Retrouver un enregistrement

Dans **Sessions**, une session enregistrée affiche une icône de lecture à côté de son identifiant, et **Enregistrées
seulement** masque les autres. Dans **Erreurs**, une occurrence dont le span porte l'attribut `session.id` a un lien **Voir le
replay de la session** qui ouvre la session au moment de l'erreur, quelques secondes avant.

### À savoir

- Un lot est envoyé toutes les quelques secondes et quand l'onglet est masqué. Un envoi échoué est abandonné, pas
  réessayé : un replay peut avoir un trou, mais ne rejoue jamais un événement deux fois. La fermeture d'un onglet peut faire
  perdre le dernier lot s'il dépasse environ 60 Ko.
- L'enregistrement s'arrête de lui-même après une heure ou quand la clé est refusée (`401` ou `403`), et `onStop` est appelé.
  Quand la clé dépasse une limite (`429`), l'enregistreur abandonne les lots pendant la durée donnée par `Retry-After`, puis
  reprend.
- Les enregistrements comptent dans la rétention des traces, et une longue page avec beaucoup de changements du DOM est
  volumineuse. Utilisez `sampleRate` sur les sites très fréquentés, ou `mode: 'onError'`. Un enregistrement compte dans les
  limites d'octets de la clé (les plafonds **Bytes** par minute et par jour dans les **Limits** de la clé, les mêmes que pour OTLP) mais pas dans ses limites
  d'événements, car un événement de replay n'est pas un enregistrement de télémétrie.
  Pour borner les enregistrements seuls, fixez les plafonds **Replay bytes** dans la même boîte de dialogue : ils ne
  s'appliquent qu'aux enregistrements, en plus des plafonds **Bytes**, donc les journaux, traces et métriques ne sont jamais
  refusés parce que le replay a épuisé son budget.
- Au-delà de 40 Mo d'enregistrement stocké, le lecteur avertit avant de le charger dans l'onglet.

## Vérifier que ça fonctionne

Ouvrez la page **Traces** et filtrez sur le service `my-web-app`. Un chargement de
page apparaît comme une trace `documentLoad` avec des spans enfants
`documentFetch` et `resourceFetch`. Les appels vers une API qui propage
`traceparent` apparaissent comme une seule trace couvrant les deux services.

## Limites

- Les navigateurs peuvent perdre le dernier lot à la fermeture d'un onglet.
