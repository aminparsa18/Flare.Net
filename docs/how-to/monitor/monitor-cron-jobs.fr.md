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

`GET` et `POST` fonctionnent tous deux. Un corps `POST` de 4 Kio au plus est conservé comme message (les 1 000 premiers caractères). Le point d'entrée répond `204`, `400` pour un slug ou un statut invalide, `401` pour une clé d'ingestion absente ou incorrecte, et `503` si le ping n'a pas pu être enregistré ; `curl -f` échoue donc bruyamment. Un ping pour un slug sans moniteur est accepté et ignoré.

Le tableau des moniteurs propose un bouton **Copy ping command** sur chaque ligne.

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

## Alerter sur un moniteur

Toutes les 15 secondes, `Flare.AlertWorker` écrit une jauge `checkin.up` (service `flare-checkin`, attribut `monitor` égal au slug). Alerter dessus est une alerte de métrique ordinaire : **Last** inférieur à 1, filtrée par `monitor`. Le bouton cloche de la ligne d'un moniteur ouvre le formulaire d'alerte avec exactement cela renseigné ; il ne reste qu'à choisir un canal de notification.

Pour être prévenu quand le moniteur lui-même cesse de rapporter (par exemple parce que le worker est arrêté), ajoutez **Absent data** à la règle.

## Limites

- Toute clé d'ingestion valide peut pinger n'importe quel slug. Donnez à vos tâches leur propre clé plutôt que de réutiliser une clé de navigateur.
- Le délai de grâce doit couvrir le décalage d'horloge et, pour une planification par intervalle, aussi la durée d'exécution de la tâche, car l'intervalle est mesuré à partir du dernier ping.
- Une tâche en retard est signalée jusqu'à `CheckIn__PollInterval` (15 s) après la fin de son délai de grâce ; utilisez donc des fenêtres d'alerte plus longues.
- Définissez `CheckIn__Enabled=false` sur le worker pour désactiver l'évaluation. Les pings sont toujours enregistrés.
- Les pings sont conservés un an.
