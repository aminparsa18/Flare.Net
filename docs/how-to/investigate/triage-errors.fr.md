# Comment trier les erreurs

La page **Errors** regroupe les exceptions par type et par message. Vous pouvez marquer chaque groupe comme résolu ou ignoré, l'assigner à quelqu'un, et être prévenu quand une erreur « corrigée » revient.

## Changer le statut d'un groupe

Ouvrez le menu **⋯** en fin de ligne :

- **Mark resolved** : vous pensez que c'est corrigé. Flare note les valeurs de `service.version` dans lesquelles le groupe a été vu au cours des 30 derniers jours.
- **Ignore** : masque le groupe et empêche les règles d'alerte sur le nombre d'exceptions de le compter. Choisissez la durée : jusqu'à la réouverture, 24 heures, 7 jours, ou 100 occurrences supplémentaires.
- **Reopen** : retour à Open.
- **Assign to me** / **Unassign** : indique qui s'en occupe.

Changer le statut exige le rôle Member ou Admin. Les Viewers voient le statut mais pas le menu.

## Lier un ticket

Ouvrez le menu **⋯** et choisissez **Lier un ticket…** pour coller l'URL du ticket qui suit le groupe (GitHub, GitLab, Jira ou toute adresse http(s)). La cellule de statut affiche alors un lien comme « Ticket #42 » ; **Retirer le lien du ticket** l'efface.

Si le service du groupe a un dépôt GitHub configuré pour les [liens source des traces de pile](link-exceptions-to-source-code.md), le menu propose aussi **Nouveau ticket GitHub**. Il ouvre la page de création de GitHub avec le titre et une courte description (exception, services, compteurs et un lien vers Flare) déjà remplis. Flare n'appelle pas GitHub lui-même : vous créez le ticket sous votre identité, puis collez son URL dans **Lier un ticket…**.

## Attribuer des responsables automatiquement

Cliquez sur **Règles de propriété** dans la barre d'outils pour associer des erreurs à des personnes. Chaque règle correspond à un nom de **service** ou à un **type d'exception** avec un motif insensible à la casse où `*` remplace n'importe quel texte, par exemple `payments-*` ou `*Timeout*`, et indique le nom d'utilisateur du responsable. Les règles sont évaluées de haut en bas et la première qui correspond possède le groupe, affiché « Responsable alice (règle) ». Les flèches permettent de les réordonner.

Une personne définie avec **M'assigner** l'emporte toujours sur une règle. Les règles ne sont pas enregistrées comme affectations : modifiez une règle et le responsable affiché change avec elle.

## Régressions

Un groupe résolu qui se reproduit dans une `service.version` où il n'avait pas été vu s'affiche comme **Regressed**, avec la version. Une occurrence dans une version déjà connue ne compte pas : le correctif n'a probablement pas encore atteint cette instance. Si vos services ne rapportent pas `service.version`, toute nouvelle occurrence d'un groupe résolu compte comme une régression.

## Filtrer par statut

Le filtre de statut, à côté du filtre de service, vaut **Active** par défaut, ce qui masque les groupes ignorés. Choisissez **All statuses**, ou un seul statut, pour voir le reste.

## Alertes

Les règles d'alerte sur le nombre d'exceptions ne comptent pas les groupes ignorés, et le résumé d'incident les omet. Le changement s'applique à la prochaine évaluation de la règle. Quand un ignorement expire, le groupe est de nouveau compté.

## Limites

Les groupes sont des paires type-message exactes. Une erreur dont le message contient un identifiant variable apparaît comme un nouveau groupe à chaque fois, et démarre à Open.
