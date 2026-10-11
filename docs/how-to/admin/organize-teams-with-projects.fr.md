# Organiser les équipes avec des projets

Par défaut, chaque utilisateur connecté voit tous les services, tableaux de bord, alertes et clés d'ingestion. Un **projet** donne une frontière à une équipe : un ensemble nommé de services et les personnes autorisées à les voir. Les projets exigent que l'authentification soit activée (voir [Configurer l'authentification](configure-authentication.fr.md)).

## Créer un projet

1. Connectez-vous en tant qu'Admin global et ouvrez **Settings > Projects**, puis **New project**.
2. Nommez-le et listez les services qu'il possède, un `service.name` par ligne. Un `*` final correspond à un préfixe (`checkout-*`). Un `*` seul est refusé pour qu'un projet ne puisse pas tout posséder silencieusement.
3. Ouvrez **Members** et ajoutez des utilisateurs avec un rôle de projet : **Admin**, **Member** ou **Viewer**.

Les motifs qui se chevauchent entre projets sont permis ; un utilisateur voit l'union de ses projets.

## Ce qu'un projet délimite

Dès qu'un projet existe, un utilisateur non admin ne voit que les services autorisés par ses projets, dans les logs, traces, métriques, erreurs, panneaux de tableaux de bord, SLO et le suivi en direct. Un service qui ne correspond à aucun projet n'est visible que des Admins globaux. Un non-admin sans projet ne voit rien. Les Admins globaux voient toujours tout. Restreindre les motifs d'un projet prend effet immédiatement.

Non délimités : les pages d'infrastructure qui ne sont pas indexées par service (Hosts, Kubernetes, santé de l'ingestion) et les jauges de retard au niveau du broker (lag Kafka, profondeur de file).

## Affecter tableaux de bord, alertes, SLO, vues et clés d'ingestion

Les formulaires de création des tableaux de bord, règles d'alerte, SLO et vues enregistrées ont un sélecteur **Project**, tout comme **New key** sous **Settings > Ingest keys**. Un objet avec un projet n'est visible que des membres du projet et des Admins globaux. Un objet sans projet est valable pour toute l'instance, comme avant. Pour déplacer une clé d'ingestion plus tard, utilisez le bouton dossier de sa ligne.

Écrire dans un objet d'un projet exige le rôle de projet Admin ou Member. Le rôle de projet ne peut que restreindre votre rôle global : un Viewer global ne peut pas modifier, même Member du projet. Un Admin de projet peut modifier tout tableau de bord du projet.

La gestion des clés d'ingestion reste réservée aux Admins globaux, car une clé peut ingérer sous n'importe quel `service.name`.

## Changer de projet

Dès que vous appartenez à un projet, un sélecteur de projet apparaît dans la barre du haut. Choisir un projet filtre les tableaux de bord, alertes, SLO et vues enregistrées sur celui-ci (ceux de toute l'instance restent affichés) et en fait la valeur par défaut des nouveaux objets. Cela ne change pas la télémétrie que vous pouvez interroger ; elle suit toujours vos appartenances.

Supprimer un projet masque ses objets à tous sauf aux Admins globaux, jusqu'à ce que l'un d'eux les réaffecte.
