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
