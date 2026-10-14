# Lire les traces de Flare depuis Grafana avec l'API Tempo

Flare expose un **sous-ensemble en lecture seule de l'API HTTP de Grafana Tempo** sur vos traces OTel. La source de données **Tempo** de Grafana peut ainsi retrouver une trace par identifiant, rechercher des traces et alimenter les sélecteurs de tags et de valeurs de son constructeur de requêtes. La recherche accepte une petite partie de TraceQL. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Tempo**.
3. Définissez l'**URL** sur la base de votre API Flare suivie de `/tempo` : `http://localhost:8080/tempo` dans la pile Docker autonome. Flare sert l'API sur `/tempo/api`, car `/api/traces` est déjà une route propre à Flare.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Une trace par identifiant (OTLP JSON ; envoyez "Accept: application/protobuf" pour le protobuf qu'utilise Grafana)
curl -H "Authorization: Bearer $TOKEN" $FLARE/tempo/api/traces/0af7651916cd43dd8448eb211c80319c

# Recherche TraceQL : traces où le service payment a un span en erreur de plus de 500 ms
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "payment" && status = error && duration > 500ms }' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode limit=20

# La même idée avec l'ancienne syntaxe de tags
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## Ce qui est pris en charge

| Point d'accès | Remarques |
|---|---|
| `GET /tempo/api/echo` | Test de connexion de Grafana |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON ou protobuf selon `Accept`. Les identifiants courts sont complétés par des zéros à gauche. La v2 signale `PARTIAL` pour une trace que Flare a tronquée |
| `GET /tempo/api/search` | `q` (TraceQL) ou `tags`, plus `minDuration`, `maxDuration`, `limit` (20 par défaut, 200 au plus), `start`, `end` (secondes unix ; la dernière heure par défaut) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Noms d'attributs vus dans la fenêtre ; la v2 les regroupe en `span`, `resource` et `intrinsic` ; `scope` restreint |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Valeurs vues dans la fenêtre |

Une recherche renvoie une ligne par trace : le service et le nom du span racine, son début et sa durée.

### Le sous-ensemble TraceQL

| Construction | Exemple |
|---|---|
| Attributs de span, avec portée | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| Opérateurs `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=` | `{ span.http.status_code >= 500 }` |
| Champs intrinsèques du span : `name` (`=` seulement), `status` (`=` seulement : `error`, `ok`, `unset`), `duration` | `{ name = "POST /pay" && duration >= 100ms }` |
| Champs intrinsèques de la trace, dans une requête à un seul spanset : `rootName`, `rootServiceName`, `traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| Combinaison de spansets : `&&`, `\|\|`, enfant `>`, descendant `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| Attributs sans portée | `{ .service.name = "api" }` |

Non pris en charge : les pipelines `|` (`count()`, `avg()`, `select()`, `by()`, ...), `||` à l'intérieur d'un même spanset, `kind`, `statusMessage`, `name` avec autre chose que `=`, `status !=`, la comparaison de deux champs, et les points d'accès de métriques TraceQL de Tempo.

## Différences avec Tempo

- **Les expressions régulières sont ancrées**, comme dans Tempo : `=~ "api-.*"` doit correspondre à toute la valeur.
- **La portée d'un attribut sans préfixe est devinée.** Dans Tempo, `.foo` signifie « span ou ressource ». Flare le lit dans la ressource quand le nom appartient à un espace de noms de ressource OpenTelemetry (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, `container.`, `os.`, ...) et dans le span sinon. Écrivez `span.foo` ou `resource.foo` pour être explicite.
- **`!=` correspond aussi aux spans sans l'attribut.** En TraceQL, l'attribut doit exister.
- **La recherche a besoin du span racine.** Une trace dont la racine n'a jamais été ingérée n'apparaît pas dans la liste, mais s'ouvre par identifiant.
- **Pas de détail des spans correspondants.** Les résultats listent des traces, pas les spans qui ont correspondu à l'intérieur.
- **Les listes de tags sont un échantillon.** Les noms viennent d'au plus 50 000 spans de la fenêtre, les plus fréquents d'abord. Réduisez `start` et `end` pour une liste complète.

Notes de conception : [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md).
