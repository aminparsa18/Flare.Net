# Lire les logs de Flare depuis Grafana avec l'API Loki

Flare expose un **sous-ensemble en lecture seule de l'API HTTP Loki** sur vos logs. La source de données Loki de Grafana peut donc exécuter du LogQL sur Flare : Explore, panneaux de logs, volume de logs, sélecteurs de labels, live tail, liens trace vers logs et l'application Logs Drilldown. Il couvre les sélecteurs de flux, les filtres de ligne, `| json`, `| logfmt`, `| line_format`, les filtres de labels, les fonctions qui transforment des logs en métriques (`rate`, `count_over_time`, `bytes_rate`, `bytes_over_time`), `absent_over_time` et les fonctions `unwrap` (`avg_over_time`, `quantile_over_time`, `rate_counter` et d'autres) avec `sum`, `topk` et l'arithmétique. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Loki**.
3. Définissez l'**URL** sur la base de votre API Flare (`http://localhost:8080` dans la pile Docker autonome). Flare sert l'API sur `/loki/api/v1`, là où Grafana l'attend.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Relier une trace à ses logs

Une fois la [source de données Tempo](query-with-tempo-api.fr.md) en place, ouvrez ses paramètres **Trace to logs**, choisissez la source Loki et associez le tag `service.name` au label `service_name`. Activez **Use custom query** et utilisez

```
{${__tags}} | trace_id="${__trace.traceId}"
```

Flare conserve les identifiants de trace et de span de chaque entrée sous les noms `trace_id` et `span_id`, la correspondance est donc exacte. L'option par défaut **Filter by trace ID** cherche l'identifiant dans le texte du log (`|= "<id>"`), ce qui ne trouve que les logs qui l'affichent.

## Parcourir les logs avec Logs Drilldown

L'application **Logs Drilldown** de Grafana (plugin `grafana-lokiexplore-app` ; *Drilldown > Logs* dans Grafana 12 et suivants) fonctionne avec la même source de données, sans configuration supplémentaire : la liste des services, la vue d'ensemble avec son volume par niveau, l'onglet **Fields** (les champs numériques sont tracés avec `unwrap`) et l'onglet **Patterns** lisent tous Flare. Elle s'appuie sur ces points d'accès :

| Point d'accès | Ce que Flare renvoie |
|---|---|
| `index/volume` | octets et entrées par service (ou par `targetLabels`) |
| `detected_labels` | les labels de flux de la fenêtre et le nombre de valeurs de chacun |
| `detected_fields` | les champs trouvés dans les 1000 lignes les plus récentes que la requête sélectionne : métadonnées structurées, champs `\| json` et `\| logfmt`, avec un type (`int`, `float`, `boolean`, `duration`, `bytes`, `string`) et un nombre de valeurs distinctes |
| `detected_field/<name>/values` | les valeurs d'un champ |
| `patterns` | les clusters de [motifs de logs](../../explanation/clustering.fr.md) de Flare, comptés dans le temps par niveau, avec `<_>` pour les parties variables |

Choisir un motif dans l'onglet Patterns ajoute un filtre de ligne par motif (`|> "<_> failed to connect <_>"`) à la requête ; Flare le prend en charge. Les champs viennent d'un échantillon : un champ qui n'apparaît que dans des lignes plus anciennes ou plus rares n'est pas listé.

## Suivre les logs en direct

Activez **Live** dans Grafana Explore (ou appelez `GET /loki/api/v1/tail?query=...` via un WebSocket) pour recevoir en continu les entrées qu'une requête de logs sélectionne, au fur et à mesure de leur ingestion. La requête s'exécute en mémoire sur chaque nouveau log, avec les mêmes labels, `| json`, `| logfmt` et `| line_format` qu'une requête normale. Passez `start` (en nanosecondes) pour recevoir d'abord jusqu'à `limit` entrées plus anciennes ; sans lui, seules les nouvelles entrées arrivent.

```bash
websocat -H "Authorization: Bearer $TOKEN" \
  'ws://localhost:8080/loki/api/v1/tail?query=%7Bservice_name%3D%22shop-api%22%7D%20%7C%3D%20%22error%22'
```

Si la connexion n'arrive pas à suivre, la trame suivante liste ce qu'elle a manqué sous la forme des `dropped_entries` de Loki (`[{"labels": {...}, "timestamp": "<ns>"}]`), pour les entrées que la requête aurait sélectionnées. Flare ne garde qu'un échantillon d'au plus 100 entrées perdues à la fois : une grosse rafale en signale donc moins qu'elle n'en a perdu.

Le live tail a deux limites. Les expressions régulières doivent s'exécuter en temps linéaire (ni lookahead ni références arrière), comme pour le live tail du tableau de bord ; une requête qui l'enfreint est refusée, motif cité. Et la connexion est fermée au bout de quelques minutes (`LiveTail:MaxSocketLifetime`, cinq par défaut) pour que le jeton soit revérifié ; la vue live de Grafana se reconnecte seule. Si Grafana est servi depuis une autre origine que celles de `Cors:AllowedOrigins`, rien à changer : une requête qui porte un en-tête `Authorization` ignore le contrôle d'origine.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Lignes de log : erreurs d'un service qui mentionnent un timeout
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query={service_name="shop-api"} |= "timeout" | detected_level="error"' \
  --data-urlencode limit=20 \
  $FLARE/loki/api/v1/query_range

# Volume de logs par niveau, un point par minute
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (detected_level) (count_over_time({service_name="shop-api"}[1m]))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/loki/api/v1/query_range
```

`start` et `end` acceptent des nanosecondes Unix (ce qu'envoie Grafana), des secondes ou du RFC 3339. Par défaut, c'est la dernière heure.

## Labels

Flare associe ses champs aux labels Loki comme le fait l'ingestion OTLP de Loki :

| Flare | Label Loki |
|---|---|
| service | `service_name` |
| sévérité (regroupée en trace, debug, info, warn, error, fatal, unknown) | `detected_level` (aussi `level` avant une étape `\| json`) |
| texte de sévérité | `severity_text` |
| portée d'instrumentation | `scope_name` |
| identifiant de trace, de span | `trace_id`, `span_id` |
| attribut de ressource `k8s.pod.name` | `k8s_pod_name` (les points deviennent des tirets bas) |
| attribut de log `http.method` | `http_method` |

Dans une réponse, un **flux** est `service_name` plus les attributs de ressource que Loki indexe par défaut (`k8s.namespace.name`, `k8s.pod.name`, `deployment.environment` et le reste de la liste de Loki). Tout le reste est de la **métadonnée structurée** attachée à chaque entrée, que Grafana affiche dans le détail du log sans fragmenter le flux.

Contrairement à Loki, n'importe quel label peut figurer dans le sélecteur de flux, y compris les attributs de log et les identifiants ci-dessus. `GET /loki/api/v1/labels` liste les attributs de ressource vus dans la fenêtre.

## Ce qui est pris en charge

| Construction | Exemple |
|---|---|
| Sélecteur de flux avec `=`, `!=`, `=~`, `!~` | `{service_name="api", k8s_namespace_name=~"prod-.*"}` |
| Filtres de ligne : `\|=`, `!=`, `\|~`, `!~`, avec `or` | `{service_name="api"} \|= "timeout" or "refused" != "health"` |
| `\| json`, seul ou avec des expressions | `\| json uid="user.id"` |
| `\| logfmt`, seul ou avec des expressions ; plusieurs analyseurs à la suite | `\| logfmt \| level="error"`, `\| json \| logfmt` |
| `\| logfmt --strict`, `--keep-empty` (voir ci-dessous) | `\| logfmt --strict \| __error__=""` |
| `\| line_format` (voir plus bas) | `\| line_format "{{.method}} {{.path}} {{.status \| default \"-\"}}"` |
| Filtres de ligne par motif `\|>` et `!>` | `\|> "<_> failed to connect <_>"` |
| Filtres de labels : `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, reliés par `and`, `,`, `or` et des parenthèses | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | accepté ; efface une erreur de conversion levée par un `unwrap` placé avant |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `absent_over_time` | `absent_over_time({service_name="worker"} \|= "heartbeat" [5m])` |
| `\| unwrap label`, `unwrap duration(label)`, `unwrap bytes(label)` avec `sum_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `first_over_time`, `last_over_time`, `stddev_over_time`, `stdvar_over_time`, `quantile_over_time`, chacune avec un `by`/`without` facultatif | `quantile_over_time(0.95, {service_name="api"} \| json \| unwrap latency_ms \| __error__="" [5m]) by (k8s_pod_name)` |
| `rate_counter`, avec un `by`/`without` facultatif | `rate_counter({service_name="api"} \| logfmt \| unwrap bytes_total \| __error__="" [5m])` |
| `sum`, `avg`, `min`, `max`, `count` avec `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` entre expressions et nombres, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Points d'accès : `query_range`, `query` (requêtes de métriques), `labels`, `label/<name>/values`, `series`, `index/stats`, `index/volume`, `detected_labels`, `detected_fields`, `detected_field/<name>/values`, `patterns`, `tail` (WebSocket), `status/buildinfo`. GET et POST encodé en formulaire fonctionnent tous deux.

Non pris en charge : `label_format`, `pattern`, `regexp`, `unpack`, `offset`, les opérateurs de comparaison et d'ensemble (`>`, `and`, `or`, `unless`) entre métriques, la mise en correspondance `on`/`ignoring`, et `index/volume_range`.

## Différences avec Loki

- **`| json` aplatit les clés imbriquées avec `_`**, comme Loki (`{"request":{"method":"GET"}}` devient `request_method`). Un filtre de label sur un nom aplati essaie toutes les façons de le découper en clés, donc `request_user_id` trouve `request.user.id` comme une clé littéralement nommée `request_user_id`. Dans les expressions `| json label="path"`, le chemin peut être pointé (`request.method`), entre crochets (`["request"]["method"]`, ce qu'écrit Logs Drilldown) ou utiliser des indices de tableau (`servers[0].name`).
- **Après `| json`, un champ nommé `level` l'emporte** sur le niveau dérivé de la sévérité, et on retombe sur ce dernier quand la ligne n'a pas ce champ.
- **Les regex de labels sont ancrées** (`=~"web"` ne correspond qu'à `web`), comme dans Loki ; **les regex de ligne ne le sont pas** (`|~"5\d\d"` correspond n'importe où dans la ligne). `|=` est sensible à la casse.
- **Les fenêtres des métriques sont fondées sur des seaux.** Une agrégation sur plage lit des seaux dont la largeur est le plus grand diviseur commun de la plage et du pas (le pas si cela donnerait trop de seaux), alignés sur les pas de la requête. Un pas sans ligne correspondante n'a pas d'échantillon.
- **`| line_format` ne réécrit que la ligne renvoyée.** Les modèles prennent en charge `{{.label}}`, `{{ __line__ }}`, `{{ __timestamp__ }}`, les littéraux chaîne et nombre, les pipelines et les fonctions `lower`, `upper`, `title`, `trim`, `trimAll`, `trimPrefix`, `trimSuffix`, `replace`, `trunc` et `default` (pas `if`/`range`). Le filtrage se faisant avant la réécriture de la ligne, un filtre de ligne, un analyseur ou un second `line_format` placé *après* un `line_format` est refusé, de même que `bytes_over_time` / `bytes_rate` sur une ligne réécrite. Les filtres de labels peuvent le suivre.
- **`| logfmt --strict` s'arrête à la première erreur.** Une ligne est bien formée si elle n'est faite que d'éléments `clé`, `clé=`, `clé=valeur` ou `clé="valeur entre guillemets"` (une valeur simple ne contient ni `=` ni `"`, une valeur entre guillemets seulement les échappements que Go accepte). Une ligne mal formée, et toute ligne qui est un objet JSON, garde les labels précédant l'erreur et reçoit `__error__="LogfmtParserErr"` ; le texte de `__error_details__` n'est pas fourni. `--keep-empty` est accepté et ne change rien, car un label vide équivaut à l'absence de label partout où Flare en évalue un. Toutes les étapes logfmt d'une requête doivent s'accorder sur `--strict`.
- **`rate_counter` et `absent_over_time`.** `rate_counter` traite la valeur extraite comme un compteur : l'augmentation entre le premier et le dernier échantillon, remises à zéro ajoutées, étirée vers les bords de la fenêtre comme le fait `rate` de Prometheus, par seconde ; une fenêtre de moins de deux échantillons vaut 0. `absent_over_time` renvoie une série de 1 aux pas sans ligne correspondante, étiquetée par les sélecteurs d'égalité, et aucune série si aucun pas n'est vide.
- **Les clés de `| logfmt` deviennent des labels**, tout caractère hors de `[a-zA-Z0-9_]` étant remplacé par `_` ; une clé sans valeur, ou à valeur vide, n'est pas un label ; une ligne qui est un objet JSON n'a pas de labels logfmt. Un label qui entre en collision avec un label de flux ou de métadonnée est renommé `<nom>_extracted`, comme dans Loki.
- **`unwrap` suit la règle de Loki pour les erreurs.** Une ligne sans le label n'est pas un échantillon. Une ligne dont la valeur n'est pas un nombre (ni une durée ou une taille, pour `duration()` et `bytes()`) est un échantillon de 0 portant `__error__="SampleExtractionErr"`. Un filtre placé après l'`unwrap` qui la rejette (`| __error__=""`, ce qu'écrit Logs Drilldown) la retire ; un filtre qui l'accepte (`| __error__!=""`) la garde comme série à part ; et une requête qui ne fait ni l'un ni l'autre est refusée avec le message `pipeline error: 'SampleExtractionErr' ...` de Loki : ajoutez `| __error__=""` après l'`unwrap`. Contrairement à Loki, la série ne porte pas `__error_details__`. `quantile_over_time` est calculé exactement, pas par pas, donc sa plage ne peut pas dépasser 120 pas (`[5m]` demande un pas d'au moins 2,5 secondes), et toute fonction `unwrap` sur une très longue plage avec un pas minuscule est refusée avec une demande de pas plus grand.
- **Limites.** Une requête de logs renvoie au plus 5000 lignes (`limit` vaut 100 par défaut) ; une requête de métriques échoue plutôt que de tronquer au-delà de 500 séries ou 11 000 points par série.
- **Les expressions régulières invalides** sont signalées à l'exécution de la requête sur des données, en syntaxe RE2.
- **Les requêtes instantanées** fonctionnent pour les métriques uniquement ; un sélecteur de logs demande `query_range`.

Notes de conception : [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md), [ADR-0237](../../../docs-internal/adr/0237-loki-api-logfmt-line-format-unwrap-tail-and-drilldown.md) et [ADR-0238](../../../docs-internal/adr/0238-loki-api-rate-counter-absent-strict-logfmt-unwrap-errors-dropped-entries.md).
