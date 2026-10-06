# Comment surveiller des endpoints, des ports et des certificats avec des sondes synthétiques

Les applications rendent compte d'elles-mêmes, ce qui n'aide pas quand un endpoint est injoignable ou qu'un certificat va expirer. Un **moniteur synthétique** est une sonde que Flare exécute à intervalle régulier, de l'extérieur. Son résultat est stocké sous forme de métriques ordinaires : vous créez des alertes et des graphiques dessus comme sur n'importe quelle autre métrique.

## Créer un moniteur

Ouvrez **Paramètres > Workspace > Synthetic monitors** et choisissez **New monitor**, ou utilisez l'API (`POST /api/synthetic-monitors`, Member ou Admin) avec un cookie de session ou un jeton d'accès personnel :

```bash
curl -X POST "$FLARE_API/api/synthetic-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"checkout health","kind":"Http","target":"https://shop.example.com/health","intervalSeconds":60}'
```

| Champ | Signification |
| --- | --- |
| `kind` | `Http`, `Tcp` ou `Tls`. |
| `target` | Http : une URL `http(s)` absolue. Tcp : `host:port`. Tls : `host` ou `host:port` (443 par défaut). |
| `method` | Http uniquement : `GET` (défaut), `HEAD`, `POST` ou `OPTIONS`. |
| `expectedStatus` | Http uniquement : le statut considéré comme « up ». `0` (défaut) signifie tout 2xx ou 3xx. |
| `requestHeaders` | Http uniquement : en-têtes de requête, un `Name: value` par ligne. L'API ne renvoie jamais les valeurs : elles sont lues sous la forme `********`, et envoyer `Name: ********` lors d'une mise à jour conserve la valeur stockée. |
| `requestBody` | Http uniquement, `POST` uniquement : le corps de la requête. Un en-tête `Content-Type` définit son type. |
| `bodyContains` / `bodyNotContains` | Http uniquement : le corps de la réponse doit contenir / ne doit pas contenir ce texte (sensible à la casse ; seul le premier 1 Mio est lu). Une assertion échouée enregistre `synthetic.up` à 0. |
| `intervalSeconds` | De 10 à 86400, 60 par défaut. |
| `timeoutSeconds` | De 1 à 120, 10 par défaut, sans dépasser l'intervalle. |
| `enabled` | `false` met le moniteur en pause. |

`GET`, `PUT` et `DELETE` sur `/api/synthetic-monitors/{id}` lisent, modifient et suppriment un moniteur.

## Ce qui est enregistré

Chaque sonde écrit des métriques gauge pour le service `flare-synthetic`, avec les attributs `monitor` (le nom), `kind` et `target` :

| Métrique | Valeur |
| --- | --- |
| `synthetic.up` | 1 si la sonde a réussi, 0 sinon. |
| `synthetic.duration` | Délai jusqu'à la réponse, la connexion ou la négociation, en ms. |
| `synthetic.http.status_code` | Http uniquement : le statut reçu. |
| `synthetic.cert.expiry_days` | Tls uniquement : jours avant l'expiration du certificat. |

Un délai dépassé, une erreur de connexion, une négociation TLS échouée (un certificat expiré, non approuvé ou ne correspondant pas à l'hôte la fait échouer) ou un statut inattendu enregistrent tous `synthetic.up` à 0.

Le tableau des sondes affiche le dernier résultat de chaque sonde (up ou down, avec le temps de la sonde), et `flare synthetic-monitors list` l'affiche dans le terminal. `create`, `update` et `delete` existent aussi ; voir la [référence CLI](../reference/cli-commands.fr.md).

## Sonder depuis plusieurs emplacements

Chaque `Flare.AlertWorker` nomme l'endroit d'où il sonde avec `Synthetic__Location` (par défaut `default`). Lancez un worker par région avec son propre nom, tous pointant vers les mêmes ClickHouse et Redis, par exemple `Synthetic__Location=eu-west` dans l'un et `us-east` dans l'autre.

Le champ **Emplacements de sonde** d'un moniteur (ou `flare synthetic-monitors create ... --location eu-west --location us-east`) liste les emplacements qui l'exécutent ; laissez-le vide pour l'exécuter depuis chaque worker. Chaque emplacement sonde une fois par intervalle, et chaque résultat porte un attribut `location`. Le tableau affiche un badge par emplacement dès que plusieurs ont répondu.

Une alerte sur `synthetic.up` avec **Min** inférieur à 1 se déclenche quand un emplacement quelconque voit le moniteur en panne. Pour alerter sur un quorum d'emplacements, utilisez plutôt **Last**. Chaque emplacement est sa propre série et **Last** fait la moyenne du dernier résultat de chaque série ; pour `synthetic.up`, c'est donc la fraction d'emplacements qui voient le moniteur en service :

| Se déclenche quand | Agrégation et seuil |
|--------------------|---------------------|
| un emplacement est en panne | **Min** inférieur à 1 |
| au moins la moitié des emplacements sont en panne | **Last** inférieur à 0,51 |
| tous les emplacements sont en panne | **Last** inférieur à 0,01 |

Avec quatre emplacements, **Last** inférieur à 0,76 signifie que deux ou plus sont en panne. Choisissez un seuil situé entre les fractions à distinguer.

Inutile de les calculer : le bouton cloche sur la ligne d'un moniteur ouvre le formulaire d'alerte filtré sur ce moniteur, avec un sélecteur **En panne depuis au moins N des M emplacements** qui règle **Last** et le seuil à votre place.

## Alerter sur un moniteur

Créez une règle d'alerte de métrique normale sur l'une de ces métriques, filtrée par l'attribut `monitor` :

- `synthetic.up` avec **Min** inférieur à 1 sur 3 minutes : l'endpoint était indisponible.
- `synthetic.duration` supérieur à 2000 sur 5 minutes : il est lent.
- `synthetic.cert.expiry_days` inférieur à 14 : renouvelez le certificat.
- **Absence de données** sur `synthetic.up` : le moniteur lui-même ne rapporte plus rien.

## Limites

- Les sondes s'exécutent là où tourne `Flare.AlertWorker` ; pour sonder depuis plusieurs endroits, voir [Sonder depuis plusieurs emplacements](#sonder-depuis-plusieurs-emplacements).
- Un moniteur fait envoyer des requêtes par le serveur vers sa cible, y compris des hôtes internes. Définissez `Synthetic__Enabled=false` sur le worker pour désactiver les sondes.
- `Synthetic__PollInterval` (5 s) et `Synthetic__MaxConcurrency` (20) règlent l'exécuteur.
