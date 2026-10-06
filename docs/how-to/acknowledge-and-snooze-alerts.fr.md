# Comment acquitter ou suspendre une alerte en cours

Une règle qui reste en dépassement renvoie une notification après chaque période
de cooldown. Quand quelqu'un s'en occupe déjà, ces répétitions sont du bruit.
Acquitter ou suspendre l'alerte indique à Flare de les arrêter.

## Acquitter

Sur la page **Alertes**, une règle en cours a un bouton d'acquittement (une
double coche) dans sa ligne. Ouvrez-le, ajoutez éventuellement une note, puis
choisissez **Acquitter**. Le statut de la règle indique qui l'a acquittée.

Un acquittement dure jusqu'à la fin de l'incident :

- Plus aucune notification « déclenchée » n'est envoyée tant que la règle reste
  en dépassement.
- Quand la condition revient à la normale, la notification de **résolution** est
  tout de même envoyée : toutes les personnes alertées savent que c'est réglé.
- Si la règle se redéclenche plus tard comme un nouvel incident, l'ancien
  acquittement ne s'applique pas.

## Suspendre

Dans la même fenêtre, choisissez 15 minutes, 1 heure, 4 heures ou 1 jour pour
couper les notifications répétées jusque-là. La ligne indique la fin de la
suspension. Si la règle est toujours en dépassement à l'expiration, elle notifie
de nouveau à la première évaluation après son cooldown.

## L'annuler

Tant qu'une règle est acquittée ou suspendue, le même bouton devient **Annuler
l'acquittement**. L'annuler permet au prochain dépassement de notifier
normalement.

## Via la CLI

```bash
flare alerts ack <rule-id> --note "Looking into the checkout DB"
flare alerts snooze <rule-id> --minutes 60
flare alerts unack <rule-id>
```

Trouvez l'identifiant avec `flare alerts list`. Chaque commande se termine avec le
code 1 si la règle n'existe pas ou n'est pas déclenchée.

## Via l'API

Les trois appels exigent l'accès en écriture au projet de la règle et renvoient
`409` si la règle n'est pas en cours.

```bash
# Acquitter, avec une note facultative
curl -X POST -H 'Content-Type: application/json' \
  -d '{"note":"Je regarde la base du checkout"}' \
  http://localhost:5080/api/alerts/<rule-id>/ack

# Suspendre 60 minutes (1 à 10080)
curl -X POST -H 'Content-Type: application/json' \
  -d '{"snoozeMinutes":60}' \
  http://localhost:5080/api/alerts/<rule-id>/snooze

# Annuler
curl -X DELETE http://localhost:5080/api/alerts/<rule-id>/ack
```

`GET /api/alerts/states` inclut un objet `ack` (qui, quand, type, fin de la
suspension, note) pour chaque règle acquittée ou suspendue. Si l'authentification
de Flare est désactivée, le « qui » est vide.

Pourquoi ce fonctionnement : [ADR-0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md).

## Acquitter depuis la notification

Lorsque `Alerting:PublicUrl` est défini, chaque notification d'une alerte
déclenchée ou escaladée contient un lien **Acknowledge**, également disponible
pour les modèles sous `{{ack_url}}` (Teams l'affiche comme un bouton, le webhook
générique comme `ackUrl`). Il ouvre une page qui affiche la règle et un bouton
**Acquitter** ; rien ne se passe tant que vous n'appuyez pas dessus, si bien que
les scanners de courrier et les aperçus de messagerie ne peuvent pas acquitter
une alerte en ouvrant le lien. Il n'est pas nécessaire d'être connecté : le lien
lui-même sert d'identifiant, et l'acquittement est enregistré sous
`notification link` (ou sous votre nom si vous avez une session).

Un lien est valable pour l'incident pour lequel il a été envoyé et expire après
24 heures (`Alerting:AckLinkLifetimeHours`) ; chaque notification en contient un
nouveau. Si l'alerte est résolue entre-temps, la page l'indique. Un bouton Slack
et la synchronisation des acquittements avec PagerDuty ne sont pas encore
disponibles.

Pourquoi c'est conçu ainsi : [ADR-0127](../../docs-internal/adr/0127-alert-ack-link.md).

## Escalader si personne n'acquitte

Une règle peut envoyer un incident non acquitté vers d'autres canaux. Dans le
formulaire de la règle, activez **Escalade si non acquitté**, indiquez le délai
en minutes et choisissez les canaux. Via l'API, ce sont `escalateAfterMinutes`
(1 à 10080, 0 désactive) et `escalationChannelIds`.

Quand un incident a été notifié depuis ce nombre de minutes sans acquittement,
Flare l'envoie une seule fois aux canaux d'escalade, avec `[Escalated]` devant
le nom de la règle, et l'ajoute à l'historique de la règle. Une mise en
sommeil n'arrête pas l'escalade, seul un acquittement le fait. Une fenêtre de
maintenance la retarde. Le message « Resolved » ne part que vers les canaux
propres à la règle.

L'escalade nécessite des canaux de **Notification channels** ; elle n'est donc
pas disponible pour les règles qui utilisent encore un webhook ou une adresse
e-mail en ligne.

Pourquoi ce fonctionnement : [ADR-0125](../../docs-internal/adr/0125-alert-escalation.md).

## Escalader à nouveau si personne n'acquitte

Dans les mêmes réglages d'escalade, activez **Puis escalader à nouveau**, indiquez un délai supplémentaire en minutes et choisissez les canaux. Si personne n'a acquitté l'incident passé ce délai après la première escalade, Flare l'envoie une fois de plus à ces canaux, toujours avec `[Escalated]` devant le nom de la règle. Via l'API, ce sont `secondEscalateAfterMinutes` (1 à 10080, 0 : pas de second palier) et `secondEscalationChannelIds`. Le second palier suppose le premier, et un acquittement arrête les deux. La rotation d'astreinte ne concerne que le premier palier.

Pourquoi ce fonctionnement : [ADR-0136](../../docs-internal/adr/0136-alert-multi-step-escalation.md).

## Escalader vers la personne d'astreinte

Une rotation d'astreinte est une liste de canaux de notification qui se
relaient, chacun pour une durée de garde fixe. Créez-en une dans **Paramètres >
Espace de travail > Rotations d'astreinte** : choisissez les canaux dans l'ordre
des gardes (un par personne ou équipe), la durée d'une garde en heures (un jour
fait 24, une semaine 168) et le début de la première garde. Le premier canal est
d'astreinte à partir de ce moment, le suivant prend le relais après une garde, et
la liste recommence après le dernier. La page indique qui est d'astreinte
maintenant et jusqu'à quand.

Ensuite, dans les paramètres d'escalade d'une règle, choisissez la rotation.
Quand l'incident escalade, Flare l'envoie au canal d'astreinte à cet instant, en
plus des canaux d'escalade fixes de la règle. Via l'API, c'est
`escalationRotationId` sur la règle ; les rotations elles-mêmes sont sous
`/api/oncall-rotations` (`channelIds`, `shiftHours`, `startsAt`).

Une rotation ne choisit que la cible de l'escalade. La première notification va
toujours aux canaux propres de la règle. Supprimer une rotation laisse ses
règles escalader vers leurs seuls canaux fixes. 

Pour échanger une garde une seule fois (« Priya couvre mardi »), ajoutez un
**remplacement** à la rotation : un canal, une heure de début et une heure de fin.
Tant qu'il est actif, ce canal est de garde à la place du participant prévu, les
escalades lui sont envoyées et la page l'indique comme remplacement. Une fois
terminé, le planning reprend sans changement. Si des remplacements se chevauchent,
celui qui a commencé en dernier l'emporte. Via l'API, c'est `overrides`
(`channelId`, `startsAt`, `endsAt`) sur la rotation.

Pourquoi ce fonctionnement : [ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md).
