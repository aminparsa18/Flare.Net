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

Si les [clés d'API d'ingestion](configure-authentication.fr.md#clés-api-dingestion)
sont obligatoires, l'exporteur a besoin de
`headers: { Authorization: 'Bearer <clé>' }`. Cette clé est livrée dans votre
JavaScript : tous les visiteurs peuvent la lire. Créez une clé dédiée au
navigateur et limitez-la aux origines de votre site :

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
[limites par clé](configure-authentication.fr.md#clés-api-dingestion) pour plafonner
ce qu'une clé divulguée peut envoyer.

## Vérifier que ça fonctionne

Ouvrez la page **Traces** et filtrez sur le service `my-web-app`. Un chargement de
page apparaît comme une trace `documentLoad` avec des spans enfants
`documentFetch` et `resourceFetch`. Les appels vers une API qui propage
`traceparent` apparaissent comme une seule trace couvrant les deux services.

## Limites

- Seules les traces sont couvertes ici. Les web vitals et les erreurs JavaScript
  demandent une instrumentation supplémentaire, et les traces de pile des bundles
  minifiés ne sont pas encore symbolisées.
- Les navigateurs peuvent perdre le dernier lot à la fermeture d'un onglet.
