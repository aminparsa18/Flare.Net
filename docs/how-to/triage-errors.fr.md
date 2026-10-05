# Comment trier les erreurs

La page **Errors** regroupe les exceptions par type et par message. Vous pouvez marquer chaque groupe comme résolu ou ignoré, l'assigner à quelqu'un, et être prévenu quand une erreur « corrigée » revient.

## Changer le statut d'un groupe

Ouvrez le menu **⋯** en fin de ligne :

- **Mark resolved** : vous pensez que c'est corrigé. Flare note les valeurs de `service.version` dans lesquelles le groupe a été vu au cours des 30 derniers jours.
- **Ignore** : masque le groupe et empêche les règles d'alerte sur le nombre d'exceptions de le compter. Choisissez la durée : jusqu'à la réouverture, 24 heures, 7 jours, ou 100 occurrences supplémentaires.
- **Reopen** : retour à Open.
- **Assign to me** / **Unassign** : indique qui s'en occupe.

Changer le statut exige le rôle Member ou Admin. Les Viewers voient le statut mais pas le menu.

## Régressions

Un groupe résolu qui se reproduit dans une `service.version` où il n'avait pas été vu s'affiche comme **Regressed**, avec la version. Une occurrence dans une version déjà connue ne compte pas : le correctif n'a probablement pas encore atteint cette instance. Si vos services ne rapportent pas `service.version`, toute nouvelle occurrence d'un groupe résolu compte comme une régression.

## Filtrer par statut

Le filtre de statut, à côté du filtre de service, vaut **Active** par défaut, ce qui masque les groupes ignorés. Choisissez **All statuses**, ou un seul statut, pour voir le reste.

## Alertes

Les règles d'alerte sur le nombre d'exceptions ne comptent pas les groupes ignorés, et le résumé d'incident les omet. Le changement s'applique à la prochaine évaluation de la règle. Quand un ignorement expire, le groupe est de nouveau compté.

## Limites

Les groupes sont des paires type-message exactes. Une erreur dont le message contient un identifiant variable apparaît comme un nouveau groupe à chaque fois, et démarre à Open.
