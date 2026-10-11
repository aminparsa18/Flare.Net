# Voir ce qui consomme le stockage

La page **Utilisation** montre quels services, clés d'ingestion et attributs occupent le plus de données stockées, avant de choisir l'échantillonnage ou des règles de [rétention](set-data-retention.fr.md).

## Ouvrir la page

Ouvrez **Paramètres > Espace de travail > Utilisation** (administrateurs), ou allez sur `/settings/usage`. Choisissez une fenêtre de 1, 7 ou 30 jours.

## Ce qui est affiché

| Section | Signification |
|---|---|
| Tuiles par signal | Événements stockés par signal dans la fenêtre, et taille compressée actuelle de chaque signal sur disque |
| Volume par service | Événements et événements par jour par service (50 premiers par signal), avec une taille estimée sur disque |
| Clés d'ingestion | Événements et octets acceptés pour chaque clé active durant le jour UTC en cours |
| Plus gros attributs | Clés d'attribut classées par octets clé + valeur dans un échantillon de lignes récentes |

## Lire les chiffres

- **Estimation sur disque** : la taille compressée de la table du signal répartie selon la part d'événements du service dans la fenêtre. ClickHouse ne conserve pas de taille par service ; servez-vous-en pour classer, pas pour facturer.
- **Clés d'ingestion** : jour UTC en cours uniquement, car c'est tout ce que conservent les compteurs par clé dans Redis.
- **Plus gros attributs** : échantillon d'au plus 100 000 lignes du dernier jour par source, en octets non compressés. La part est calculée au sein d'une même source.
- Les spans échantillonnés comptent une fois, tels que stockés.

## Agir

Un service qui domine un signal est candidat à une règle de [rétention](set-data-retention.fr.md) plus courte ou à l'échantillonnage des traces. Un gros attribut rarement interrogé peut être supprimé avec une [règle de pipeline](manage-pipeline-rules.fr.md).
