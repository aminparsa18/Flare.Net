# Comment surveiller des tâches cron et des tâches planifiées avec des check-ins

Une tâche qui plante écrit une erreur sur laquelle vous pouvez alerter. Une tâche qui ne démarre jamais n'écrit rien : le planificateur est mort, le CronJob a été supprimé, l'hôte était arrêté à 02:00. Un **moniteur de check-in** couvre ce cas. La tâche appelle une URL de Flare quand elle démarre et quand elle se termine, et Flare alerte quand un appel est en retard ou signale un échec.

## Créer un moniteur

Ouvrez **Paramètres > Workspace > Cron and job check-ins** et choisissez **New check-in monitor**, ou utilisez l'API (`POST /api/checkin-monitors`, Member ou Admin) avec un cookie de session ou un jeton d'accès personnel :

```bash
curl -X POST "$FLARE_API/api/checkin-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"Nightly backup","cronExpression":"0 2 * * *","timeZone":"Europe/Berlin","graceSeconds":600,"maxRuntimeSeconds":3600}'
```

| Champ | Signification |
| --- | --- |
| `name` | Obligatoire. |
| `slug` | Le nom dans l'URL de ping : minuscules, chiffres, `-` et `_`, 64 caractères au plus. Déduit du nom s'il est omis, et fixe après la création. |
| `cronExpression` | Une expression cron à cinq champs (minute heure jour-du-mois mois jour-de-la-semaine) ou une macro comme `@daily`, lue dans `timeZone`. Renseignez ce champ ou `intervalSeconds`. |
| `timeZone` | Un nom IANA comme `UTC` (par défaut) ou `Europe/Berlin`. Utilisé avec `cronExpression` uniquement. |
| `intervalSeconds` | L'autre type de planification : un ping est attendu toutes les N secondes, de 60 à 2678400. Il est mesuré à partir du dernier ping. |
| `graceSeconds` | Le retard toléré après l'heure attendue avant que la tâche soit considérée en retard. 300 par défaut. |
| `maxRuntimeSeconds` | Facultatif. Une exécution démarrée et non terminée dans ce délai est considérée en panne. 0 (par défaut) signifie sans limite. |
| `enabled` | `false` met le moniteur en pause. |

`GET`, `PUT` et `DELETE` sur `/api/checkin-monitors/{id}` lisent, modifient et suppriment un moniteur. Tout utilisateur connecté peut lister les moniteurs ; les créer, les modifier et les supprimer exige le rôle Member ou Admin.

Depuis un terminal, `flare checkin-monitors create "Nightly backup" --cron "0 2 * * *" --timezone Europe/Berlin --grace 600 --max-runtime 3600` fait la même chose sur un Flare local, et `list`, `update` et `delete` gèrent le reste ; `list` affiche l'état de chaque moniteur ainsi que son dernier ping et le prochain attendu. Voir la [référence CLI](../../reference/cli-commands.fr.md).

Avec le provider Terraform / OpenTofu de Flare, un moniteur est une ressource `flare_checkin_monitor` (définissez `cron_expression` ou `interval_seconds`, pas les deux) :

```hcl
resource "flare_checkin_monitor" "nightly_backup" {
  name            = "Nightly backup"
  cron_expression = "0 2 * * *"
  time_zone       = "Europe/Berlin"
  grace_seconds   = 600
}
```

## Envoyer des check-ins depuis la tâche

La tâche appelle `Flare.Ingest` sur le port HTTP (4318 par défaut) avec une [clé d'ingestion](../admin/configure-authentication.fr.md), le même identifiant que vos exporteurs OpenTelemetry. Un chemin nu signale un succès ; `/start` et `/fail` sont les deux autres :

```bash
FLARE="https://ingest.example.com"
AUTH="Authorization: Bearer $FLARE_INGEST_KEY"

curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup/start"
./backup.sh && curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup" \
           || curl -fsS -m 10 -H "$AUTH" --data "backup.sh exited $?" "$FLARE/v1/checkins/nightly-backup/fail"
```

| Chemin | Signification |
| --- | --- |
| `/v1/checkins/{slug}` ou `/v1/checkins/{slug}/ok` | La tâche s'est terminée avec succès. |
| `/v1/checkins/{slug}/start` | La tâche a démarré. Facultatif ; envoyez-le pour repérer une tâche qui démarre puis se bloque. |
| `/v1/checkins/{slug}/fail` | La tâche s'est terminée en échec. |

`GET` et `POST` fonctionnent tous deux. Un corps `POST` de 4 Kio au plus est conservé comme message (les 1 000 premiers caractères). Le point d'entrée répond `204`, `400` pour un slug ou un statut invalide, `401` pour une clé d'ingestion absente ou incorrecte, et `503` si le ping n'a pas pu être enregistré ; `curl -f` échoue donc bruyamment. Un ping pour un slug sans moniteur est accepté et ignoré, sauf s'il demande à Flare de créer le moniteur (section suivante).

Le tableau des moniteurs propose un bouton **Copy ping command** sur chaque ligne.

## Créer le moniteur depuis le premier ping

Une tâche peut créer son propre moniteur au lieu d'attendre qu'on le crée. Ajoutez `create=1` et un planning à l'URL du ping :

```bash
curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup/start?create=1&cron=0%202%20*%20*%20*&tz=Europe/Berlin&grace=600&name=Nightly%20backup"
```

| Paramètre | Signification |
| --- | --- |
| `create` | `1` ou `true` demande le moniteur. Sans lui, les autres paramètres sont ignorés. |
| `cron` ou `interval` | Le planning, un seul des deux, comme dans le tableau ci-dessus. Encodez les espaces d'une expression cron dans l'URL. |
| `tz` | Le fuseau IANA de `cron`. `UTC` par défaut. |
| `grace`, `max_runtime` | En secondes, comme `graceSeconds` et `maxRuntimeSeconds`. 300 et 0 par défaut. |
| `name` | Le nom affiché. Le slug par défaut. |

Le moniteur est créé une seule fois, au premier ping de ce type, et le ping est enregistré comme d'habitude. Si un moniteur portant ce slug existe déjà, les paramètres sont ignorés : les pings suivants ne modifient donc jamais un planning, modifiez-le dans le tableau de bord, l'API ou Terraform. Un planning invalide renvoie `400` et rien n'est enregistré. Vous pouvez laisser `create=1` sur les pings de la tâche ou le retirer après la première exécution.

La création automatique se désactive avec `CheckIns__AutoCreate=false` sur `Flare.Ingest` (ces pings renvoient alors `403`), et `CheckIns__MaxMonitors` (500 par défaut) plafonne le nombre de moniteurs, afin qu'une clé d'ingestion divulguée ne puisse pas remplir la table. Sans `create=1`, une faute de frappe dans un slug ne crée toujours rien.

## Comment l'état est déterminé

Le prochain ping est attendu à la première heure planifiée suivant le dernier ping. Chaque moniteur est dans l'un de ces états :

| État | Quand | `checkin.up` |
| --- | --- | --- |
| Waiting for first check-in | Aucun ping encore, et le premier n'est pas en retard. | 1 |
| On schedule | La dernière exécution est terminée et la suivante n'est pas en retard. | 1 |
| Running | Une exécution a démarré sans se terminer, dans la limite de `maxRuntimeSeconds` si définie. | 1 |
| Late | Le prochain ping n'est pas arrivé dans le délai de grâce. | 0 |
| Failed | La dernière exécution a signalé `fail`. L'état reste en échec jusqu'à ce qu'une exécution signale un succès. | 0 |
| Running too long | Une exécution a démarré sans se terminer dans `maxRuntimeSeconds`. | 0 |
| Paused | Le moniteur est désactivé. Il n'écrit aucune métrique. | aucune |

Enregistrer un moniteur relance son horloge : le premier ping est attendu à la prochaine occurrence de la planification à partir de ce moment, de sorte que modifier ou réactiver un moniteur ne le signale jamais en retard immédiatement. Une tâche qui démarre sans jamais se terminer, sans `maxRuntimeSeconds`, est signalée en retard une fois sa prochaine heure planifiée et le délai de grâce écoulés.

## Voir les exécutions d'un moniteur

Le bouton d'historique de la ligne d'un moniteur liste ses exécutions récentes, de la plus récente à la plus ancienne : heure de début, durée, succès, échec ou jamais terminée, et le message envoyé. `GET /api/checkin-monitors/{id}/runs?limit=50` renvoie la même liste (200 au maximum).

Les exécutions sont reconstituées à partir des pings que vous envoyez déjà : un ping de fin (`ok` ou `fail`) clôt le dernier début qui le précède, et un second `start` alors qu'un autre est ouvert laisse le premier marqué **Jamais terminée**. Une tâche qui n'envoie qu'un ping de fin apparaît aussi, sans durée. Les pings ne portent pas d'identifiant d'exécution : deux exécutions simultanées de la même tâche peuvent donc être mal appariées.

## Alerter sur un moniteur

Toutes les 15 secondes, `Flare.AlertWorker` écrit une jauge `checkin.up` (service `flare-checkin`, attribut `monitor` égal au slug). Alerter dessus est une alerte de métrique ordinaire : **Last** inférieur à 1, filtrée par `monitor`. Le bouton cloche de la ligne d'un moniteur ouvre le formulaire d'alerte avec exactement cela renseigné ; il ne reste qu'à choisir un canal de notification.

Pour être prévenu quand le moniteur lui-même cesse de rapporter (par exemple parce que le worker est arrêté), ajoutez **Absent data** à la règle.

## Limites

- Toute clé d'ingestion valide peut pinger n'importe quel slug et, sauf désactivation, créer un moniteur avec `create=1`. Donnez à vos tâches leur propre clé plutôt que de réutiliser une clé de navigateur.
- Le délai de grâce doit couvrir le décalage d'horloge et, pour une planification par intervalle, aussi la durée d'exécution de la tâche, car l'intervalle est mesuré à partir du dernier ping.
- Une tâche en retard est signalée jusqu'à `CheckIn__PollInterval` (15 s) après la fin de son délai de grâce ; utilisez donc des fenêtres d'alerte plus longues.
- Définissez `CheckIn__Enabled=false` sur le worker pour désactiver l'évaluation. Les pings sont toujours enregistrés.
- Les pings sont conservés un an.
