# Définir des SLO et être alerté quand le budget d'erreurs se consomme

Un objectif de niveau de service (SLO) est une promesse de fiabilité mesurée sur une fenêtre glissante : par exemple, « 99,5 % des requêtes `POST /checkout` réussissent sur 28 jours ». Les 0,5 % qui peuvent échouer forment le **budget d'erreurs**. Flare montre ce qu'il en reste et vous alerte quand il se consomme trop vite, avant qu'il ne soit épuisé.

## Définir un SLO

1. Ouvrez **SLOs** dans le menu utilisateur, puis **New SLO**.
2. Choisissez l'objectif :
   - **Disponibilité** : une requête est bonne sauf si son span s'est terminé en erreur.
   - **Latence** : une requête est bonne quand elle se termine dans un seuil. Le seuil est l'une des valeurs 50, 100, 250, 500, 1000, 2500, 5000 ou 10000 ms.
3. Choisissez le service et, si vous le souhaitez, un seul point d'accès (un nom de span d'entrée, comme une route). Sans point d'accès, tous les spans d'entrée du service comptent.
4. Fixez la cible (par exemple 99,5) et la fenêtre (de 7 à 90 jours, 28 par défaut).

Une « requête » est un span d'entrée : un span serveur ou consommateur, ou un span racine pour un travail qui démarre sa propre trace. Les spans clients et internes ne comptent pas.

## Lire le budget

La liste montre pour chaque SLO le SLI courant (le pourcentage de bonnes requêtes), la part du budget d'erreurs restante et son taux de consommation sur la dernière heure. Ouvrez-en un pour voir le budget sur la fenêtre et le taux de consommation sur 5 minutes, 30 minutes, 1 heure, 6 heures et 24 heures.

Un **taux de consommation** de 1x dépense exactement tout le budget à la fin de la fenêtre. 14,4x dépense 2 % d'un budget de 30 jours en une heure. Une fenêtre sans requête affiche `-` : l'absence de trafic n'est ni une violation ni un 100 % sain.

## Alerter sur le taux de consommation

Dans le détail d'un SLO, choisissez un ou plusieurs canaux de notification puis **Create burn-rate alerts**. Flare crée deux règles, adaptées à la fenêtre du SLO :

| Règle | Se déclenche quand | Sévérité | Pour une fenêtre de 30 jours |
| --- | --- | --- | --- |
| Consommation rapide | 2 % du budget part en 1 heure, confirmé sur les 5 dernières minutes | Critical | taux de 14,4x ou plus |
| Consommation lente | 5 % du budget part en 6 heures, confirmé sur les 30 dernières minutes | Warning | taux de 6x ou plus |

Une règle ne se déclenche que si le taux de consommation atteint son seuil sur **les deux** fenêtres. La fenêtre longue montre que la consommation est durable, la courte qu'elle se poursuit, de sorte que l'alerte se résout peu après la fin du problème.

Ce sont des règles d'alerte ordinaires (condition **SLO burn rate**). Elles utilisent vos canaux de notification, leur délai de répétition, les fenêtres de maintenance et les notifications de résolution comme toute autre règle, et apparaissent dans la page Alerts. Pour en modifier une, supprimez-la et recréez-la depuis la page SLO. Recréer la paire n'ajoute que la règle manquante.

## Remplir l'historique

Les SLO lisent un pré-agrégat par minute, rempli à mesure que les spans sont ingérés. Après une mise à jour, il ne contient que les spans reçus depuis ; une longue fenêtre commence donc courte. Pour le remplir avec les spans déjà présents, exécutez une fois l'`INSERT ... SELECT` du commentaire en tête de `db/clickhouse/0049_slos.sql`.

## Limites

- La latence est comptée sur toutes les requêtes, erreurs comprises. Une erreur rapide compte comme bonne pour la latence ; associez donc un SLO de latence à un SLO de disponibilité pour le même point d'accès.
- Nommez vos spans avec des modèles de route (`GET /orders/{id}`), pas des URL concrètes : chaque nom de span distinct est un point d'accès à part.
- Supprimer un SLO conserve ses règles de consommation, mais elles cessent d'être évaluées. Supprimez-les d'abord depuis la page SLO.
- L'API est `GET/POST /api/slos`, `GET/PUT/DELETE /api/slos/{id}` et `GET /api/slos/{id}/status`. La lecture demande un utilisateur connecté, l'écriture le rôle Member ou Admin.
