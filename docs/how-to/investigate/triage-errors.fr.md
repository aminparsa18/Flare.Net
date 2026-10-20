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

Un troisième type, **Chemin de fichier**, compare les fichiers source de la trace de pile du groupe avec la syntaxe de chemins de [CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners) : `/src/payments/` (un dossier à la racine du dépôt), `*.sql` (à toute profondeur), `docs/*.md`, `src/**/Charge.cs`. Flare lit les chemins d'une trace de pile d'exemple par groupe ; seuls les cadres qui portent une information de fichier comptent (pour .NET, il faut des symboles ou un build déterministe avec chemins source). L'emplacement du dépôt sur la machine de build n'importe pas : un motif est essayé sur chaque fin de chemin.

**Importer CODEOWNERS** transforme un fichier CODEOWNERS collé en règles Chemin de fichier. Chaque ligne retient son premier responsable sans le `@` initial, et l'ordre est inversé car dans CODEOWNERS la dernière ligne l'emporte alors que Flare retient la première correspondance. Les responsables ne servent que s'ils sont identiques aux noms d'utilisateur Flare ; relisez la liste avant d'enregistrer.

Une personne définie avec **M'assigner** l'emporte toujours sur une règle. Les règles ne sont pas enregistrées comme affectations : modifiez une règle et le responsable affiché change avec elle.

## Régressions

Un groupe résolu qui se reproduit dans une `service.version` où il n'avait pas été vu s'affiche comme **Regressed**, avec la version. Une occurrence dans une version déjà connue ne compte pas : le correctif n'a probablement pas encore atteint cette instance. Si vos services ne rapportent pas `service.version`, toute nouvelle occurrence d'un groupe résolu compte comme une régression.

## Filtrer par statut

Le filtre de statut, à côté du filtre de service, vaut **Active** par défaut, ce qui masque les groupes ignorés. Choisissez **All statuses**, ou un seul statut, pour voir le reste.

**Mes erreurs**, à côté, n'affiche que les groupes dont vous êtes responsable : ceux qui vous sont assignés ou, sans assigné, ceux qu'une règle de propriété vous attribue. Le bouton est masqué quand l'authentification est désactivée, faute d'utilisateur courant. Le choix est mémorisé dans ce navigateur, et `/errors?mine=1` ouvre la page avec le filtre actif.

## Alertes

Les règles d'alerte sur le nombre d'exceptions ne comptent pas les groupes ignorés, et le résumé d'incident les omet. Le changement s'applique à la prochaine évaluation de la règle. Quand un ignorement expire, le groupe est de nouveau compté.

Une notification d'alerte sur le nombre d'exceptions indique aussi qui est responsable de l'erreur. Elle examine les trois groupes les plus fréquents de la fenêtre et ajoute une ligne `Owner:` avec l'assigné de chaque groupe ou, à défaut, la première règle de propriété qui correspond (service, type d'exception ou chemin de la trace de pile), sans répéter un nom. La même valeur est disponible sous la forme `{{owner}}` dans les [modèles de notification](../alerting/share-alert-notification-templates.fr.md), vide si personne ne possède les groupes, et le résumé d'incident par IA la reçoit aussi.

## Modifier le regroupement des erreurs

Cliquez sur **Règles de regroupement** dans la page Erreurs (Member ou Admin). Trois réglages s'appliquent dans cet ordre :

1. **Réduire les identifiants, nombres et valeurs entre guillemets** est la normalisation automatique décrite plus haut. Désactivez-la si elle fusionne des erreurs que vous voulez distinguer, comme `HTTP 404` et `HTTP 500`.
2. Les règles **Remplacer** réécrivent une partie de chaque message avec une expression régulière ([syntaxe RE2](https://github.com/google/re2/wiki/Syntax)) et un remplacement, où `\1` désigne le premier groupe de capture. `user [a-z]+` remplacé par `user <name>` fait de `user alice denied` et `user bob denied` un seul groupe. Les règles s'exécutent dans l'ordre de la liste, après la normalisation automatique, sur tous les messages quel que soit le type d'exception.
3. **Fusionner** intègre un groupe à un autre du même type d'exception. Ouvrez le menu d'une ligne, choisissez **Fusionner avec un autre groupe…** puis la cible. La fusion est enregistrée comme règle : elle s'applique aussi aux occurrences futures et apparaît dans la même fenêtre.

L'enregistrement vérifie chaque expression. Un motif qui ne compile pas, qui correspond à une chaîne vide ou dont le remplacement correspond de nouveau au motif est refusé, avec la raison.

L'état de triage suit les groupes. Quand les règles changent, l'état résolu, ignoré ou assigné passe au groupe auquel appartient désormais le message ; si plusieurs groupes n'en forment plus qu'un, le plus récemment modifié l'emporte. Retirer une règle ou désactiver la normalisation sépare de nouveau les groupes, mais ne restaure pas l'état qu'avait un groupe fusionné : ces groupes redémarrent à Open.

## Limites

Un groupe est un type associé à un message normalisé. Avant le regroupement, Flare remplace les parties variables d'un message par des marqueurs : GUID (`<guid>`), valeurs hexadécimales `0x` (`<hex>`), horodatages ISO-8601 (`<ts>`), valeurs entre guillemets (`'<s>'`) et nombres isolés (`<n>`). `Order 4812 not found` et `Order 77 not found` forment un seul groupe, affiché `Order <n> not found` ; ouvrez le groupe pour voir le message propre à chaque occurrence. Les identifiants qui contiennent simplement des chiffres, comme `HTTP2` ou `sha256`, ne sont pas modifiés.

Un message dont la partie variable est un mot et non un nombre, un identifiant ou une valeur entre guillemets (un nom d'utilisateur, un nom d'hôte) n'est pas réduit automatiquement ; voir [Modifier le regroupement des erreurs](#modifier-le-regroupement-des-erreurs).

L'état de triage suit le groupe normalisé. La mise à jour réattribue l'état enregistré sous les anciens messages exacts (migration `0087`) : quand plusieurs anciens groupes n'en forment plus qu'un, le plus récemment modifié l'emporte. Les règles d'alerte sur le nombre d'exceptions comparent de la même façon : une règle dont le message est `Order 4812 not found` couvre désormais aussi les autres identifiants de commande.
