# Comment suivre les versions et les erreurs qu'elles introduisent

Marquer une version indique à Flare qu'une version d'un service a été déployée, avec son commit et son heure de déploiement. La page **Releases** liste ensuite, pour chaque version, les groupes d'exceptions apparus pour la première fois avec elle.

## Marquer une version depuis votre pipeline

Appelez l'API depuis l'étape de déploiement avec un jeton d'accès personnel (**Paramètres > Jetons d'accès**) d'un Member ou d'un Admin :

```bash
curl -X PUT "$FLARE_URL/api/releases" \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"service":"orders-api","version":"2.4.0","commit":"'"$GIT_SHA"'","url":"'"$RUN_URL"'"}'
```

| Champ | Signification |
| --- | --- |
| `service` | Le `service.name` du service. Obligatoire |
| `version` | Exactement le `service.version` que le service rapporte. Obligatoire |
| `commit`, `url`, `notes` | Facultatifs. `url` doit être un lien http(s) vers le commit, la pull request ou l'exécution du pipeline |
| `deployedAt` | Quand le déploiement a eu lieu (ISO 8601). Par défaut : maintenant |

Marquer à nouveau le même service et la même version met à jour le marqueur. Sur une pile locale, `flare releases mark orders-api 2.4.0 --commit $SHA` fait la même chose, `flare releases list --service orders-api` affiche le résultat et `flare releases delete` supprime un marqueur. Supprimer un marqueur ne touche jamais à la télémétrie.

## Lire la page Releases

Ouvrez **Releases** dans le menu et choisissez un service. Chaque ligne est une version marquée avec son heure de déploiement, son commit et **Nouvelles erreurs** : le nombre de groupes d'exceptions dont la première occurrence enregistrée date de cette version. Développez une ligne pour voir ces groupes, les plus fréquents d'abord, chacun avec un lien vers la page Erreurs limitée au service et à la version.

Un groupe est considéré comme nouveau s'il n'avait aucune occurrence dans les 30 jours précédant le déploiement. Un groupe resté silencieux plus longtemps puis revenu apparaît comme nouveau dans la version où il est revenu.

## Régressions

Un groupe que vous avez résolu sur la [page Erreurs](triage-errors.fr.md) passe à **Regressed** quand il réapparaît dans une version où il n'avait pas été vu. Cela fonctionne avec `service.version` seul, avec ou sans marqueurs de version. La page Releases répond à l'autre question : quelles erreurs une version donnée a ajoutées.

## Limites

- Seuls les événements d'exception des spans sont comptés, regroupés par type et message exacts, comme sur la page Erreurs.
- La télémétrie sans `service.version` n'est attribuée à aucune version.
- Les compteurs de nouvelles erreurs sont calculés à partir de vos spans au chargement de la page, pour le service sélectionné uniquement.
