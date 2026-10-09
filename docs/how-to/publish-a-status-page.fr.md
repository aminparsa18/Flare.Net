# Comment publier une page de statut

Une **page de statut** est une page en lecture seule sur la santé de vos services, que toute personne disposant du lien peut ouvrir sans compte Flare. Elle affiche l'état actuel de chaque composant et sa disponibilité quotidienne sur les 90 derniers jours. Un composant est un [moniteur synthétique](synthetic-monitoring.fr.md) ou un [SLO](define-slos.fr.md), et la page n'affiche que le nom que vous lui donnez.

## Créer une page

Ouvrez **Settings > Workspace > Status pages** (Admin) et choisissez **New status page** :

1. Donnez-lui un **titre** et un **slug d'URL** (lettres minuscules, chiffres et tirets). La page est servie à `/status/<slug>` sur le tableau de bord.
2. Ajoutez des **composants** : choisissez des moniteurs et des SLO, et modifiez le nom public de chacun. Les cibles des moniteurs et les noms des SLO ne sont jamais affichés.
3. Activez **Published**. Les pages sont créées non publiées, et une page non publiée répond 404.

Vous pouvez faire la même chose avec l'API (`/api/status-pages`, Admin) avec un cookie de session ou un jeton d'accès personnel :

```bash
curl -X POST "$FLARE_API/api/status-pages" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"slug":"status","title":"Acme status","enabled":true,
       "components":[{"name":"Website","kind":"Monitor","refId":"<monitor id>"},
                     {"name":"Checkout API","kind":"Slo","refId":"<slo id>"}]}'
```

`GET`, `PUT` et `DELETE` sur `/api/status-pages/{id}` lisent, modifient et suppriment une page.

## Ce que voient les visiteurs

| État | Composant moniteur | Composant SLO |
| --- | --- | --- |
| Operational | Tous les emplacements ayant répondu récemment le voient disponible. | Le budget d'erreurs n'est pas consommé. |
| Degraded | Certains emplacements le voient indisponible. | Le budget d'erreurs est dépassé. |
| Outage | Tous les emplacements le voient indisponible. | |
| No data | Désactivé, jamais sondé, ou aucun résultat depuis trois intervalles. | Aucun trafic dans la fenêtre du SLO. |

La bannière du haut affiche le pire état parmi les composants. Chaque composant a aussi une barre de 90 jours au plus, un segment par jour UTC : vert à 99,9 % ou plus, ambre à 95 % ou plus, rouge en dessous. Le jour d'un moniteur est la part de sondes réussies. Le jour d'un SLO est la part d'événements corrects, et un SLO n'affiche pas plus de jours que sa propre fenêtre.

## À savoir

- La page est publique pour toute personne pouvant joindre l'instance. Dépubliez-la ou supprimez-la pour désactiver le lien immédiatement.
- Le résultat est mis en cache 30 secondes, un changement peut donc mettre ce temps à apparaître. La page se rafraîchit toute seule chaque minute.
- Renommer un moniteur repart de zéro pour son historique, car les résultats de sonde sont stockés sous le nom du moniteur.
- Un composant dont le moniteur ou le SLO a été supprimé reste sur la page avec **No data** ; retirez-le dans l'éditeur.
