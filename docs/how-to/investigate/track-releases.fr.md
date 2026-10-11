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

Un groupe que vous avez résolu sur la [page Erreurs](triage-errors.fr.md) passe à **Regressed** quand il réapparaît dans une version où il n'avait pas été vu. Cela fonctionne avec `service.version` seul, avec ou sans marqueurs de version. La page Releases répond aux deux autres questions : quelles erreurs une version donnée a ajoutées, et quels groupes résolus y sont revenus. La colonne **Regressions** compte les seconds, et déplier une ligne les liste en premier, avec la date de résolution. Un groupe n'est compté que s'il s'est produit dans ce service sous cette version.

## Marqueurs de déploiement sur les graphiques

Le graphique de volume des Logs et le graphique des Metrics tracent une ligne verticale en pointillés à chaque version déployée dans la plage de temps, pour les services filtrés (tous si aucun filtre). Survolez une ligne pour voir le service, la version et l'heure. Les 12 plus récentes au maximum sont tracées. Les panneaux Metrics et Logs des tableaux de bord les affichent aussi.

## Alerter sur une mauvaise version

Créez une règle d'alerte avec la condition **Nouvelles erreurs après une version**. Choisissez les services à surveiller (tous si aucun n'est choisi), un seuil comme `>= 1` et une fenêtre comme 3600 secondes. La règle examine les versions marquées pendant la fenêtre et se déclenche lorsque la pire d'entre elles a introduit au moins ce nombre de nouveaux groupes d'exceptions ; elle reste donc silencieuse entre deux déploiements. La notification nomme la version qui a introduit le plus de nouveaux groupes et renvoie vers la page Releases. Elle n'a pas d'option « pas de données », car l'absence de nouvelles erreurs est l'état normal.

Via l'API, c'est `conditionKind: "ReleaseErrors"` avec `exceptionCondition: { "exceptionType": "", "filter": { "services": ["orders-api"] } }` ; seuls les services du filtre sont lus.

## Limites

- Seuls les événements d'exception des spans sont comptés, regroupés par type et message exacts, comme sur la page Erreurs.
- La télémétrie sans `service.version` n'est attribuée à aucune version.
- Les compteurs de nouvelles erreurs sont calculés à partir de vos spans au chargement de la page, pour le service sélectionné uniquement.
