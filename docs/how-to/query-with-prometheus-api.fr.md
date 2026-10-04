# Interroger Flare avec Grafana ou l'API Prometheus

Flare expose un **sous-ensemble en lecture seule de l'API HTTP Prometheus** sur vos métriques OTel. Grafana, `promtool` et `prometheus-adapter` (HPA Kubernetes sur métriques personnalisées) peuvent donc l'utiliser comme source de données Prometheus. Il couvre les sélecteurs, `rate`/`increase`, `sum|avg|min|max|count` et `histogram_quantile`. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Prometheus**.
3. Définissez l'**URL** sur la base de votre API Flare (`http://localhost:8080` dans la pile Docker autonome). Flare sert l'API sur `/api/v1`, là où Grafana l'attend.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Requête instantanée
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# Requête sur plage
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## Noms de métriques et de labels

Flare convertit les noms OTel selon les conventions Prometheus :

| OTel | Prometheus |
|---|---|
| `http.server.request.duration` (histogramme, unité `s`) | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |
| `http.server.requests` (sum) | `http_server_requests_total` |
| `process.memory` (gauge, unité `By`) | `process_memory_bytes` |
| attribut `http.route` | label `http_route` |
| ressource `service.name` | label `service_name` |

Trouvez les noms exacts avec `GET /api/v1/label/__name__/values`.

## Ce qui est pris en charge

| Construction | Exemple |
|---|---|
| Sélecteurs, avec `=`, `!=`, `=~`, `!~` | `up{service_name="api", code!="200"}` |
| `rate`, `increase` | `rate(requests_total[5m])` |
| `sum`, `avg`, `min`, `max`, `count` avec `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `histogram_quantile` sur `rate`/`increase`, éventuellement dans `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| `_sum` et `_count` d'un histogramme sous `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Arithmétique entre nombres | `1+1` |

Points d'accès : `query`, `query_range`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Non pris en charge : opérateurs entre séries (`a / b`), `offset`, `@`, sous-requêtes, autres fonctions, `topk`, `quantile`, règles d'enregistrement et écriture de données. Pour un taux d'erreur, définissez un [SLO](define-slos.fr.md).

## Différences avec Prometheus

- **Les compteurs exigent `rate()` ou `increase()`.** Flare stocke des incréments par intervalle ; un compteur nu comme `requests_total` est donc refusé, avec un renvoi vers `rate()`. Une jauge utilisée sous `rate()` est lue comme un compteur, c'est ainsi qu'arrivent les métriques Prometheus `*_total` sans type.
- **Les horodatages sont des débuts de bucket.** Les échantillons tombent sur des multiples du pas, pas sur `start + k*step`. Une fenêtre `[5m]` glisse sur `round(5m / pas)` buckets, au moins un.
- **200 séries par sélecteur.** Un sélecteur correspondant à plus de séries renvoie les 200 plus grosses et ajoute une entrée `warnings`. Ajoutez des matchers de label pour restreindre. Les matchers `!=` et regex s'appliquent après cette limite.
- **Les requêtes instantanées** regardent les cinq dernières minutes.
- **`labels` et les valeurs de label sans `match[]`** échantillonnent les dix métriques ayant le plus de séries. Passez `match[]` pour une réponse complète.

Notes de conception : [ADR-0109](../../docs-internal/adr/0109-prometheus-query-api.md).
