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

## Gérer les pages depuis la CLI ou Terraform

Les deux exigent un rôle Admin.

```bash
flare status-pages create acme-public --title 'Acme status' \
  --component 'API=slo:<slo-id>' --component 'Website=monitor:<monitor-id>' --enabled true
flare status-pages list
flare status-pages update <id> --enabled false
```

`update` ne modifie que les options passées ; `--component` remplace tous les composants.

Avec le provider Terraform / OpenTofu de Flare :

```hcl
resource "flare_status_page" "public" {
  slug    = "acme-public"
  title   = "Acme status"
  enabled = true
  components = [
    { name = "API", kind = "Slo", ref_id = flare_slo.api.id },
  ]
  subscriber_channel_ids = [flare_notification_channel.oncall.id]
}
```

## Publier des incidents

L'état calculé dit ce qui fonctionne ; un incident explique pourquoi, avec vos mots. Dans **Paramètres > Pages de statut**, ouvrez la boîte **Incidents** d'une page, signalez un incident avec un titre, un statut (Investigating, Identified, Monitoring ou Resolved) et un message, puis publiez des mises à jour au fil de l'eau. Une mise à jour **Resolved** le clôt.

Les incidents ouverts s'affichent au-dessus des composants sur la page publique, et les incidents résolus y restent 14 jours. Les incidents ne modifient ni l'état calculé ni la bannière. L'API est `/api/status-pages/{id}/incidents` ; le texte des mises à jour est public, n'y mettez donc aucun secret. Cochez les composants touchés par un incident et la page publique les nomme à côté ; le champ `components` de l'API prend leurs ids de moniteur ou de SLO, et une mise à jour ultérieure peut modifier la liste. C'est une simple étiquette : l'état calculé du composant ne change pas.

### Notifier des canaux

Une page peut prévenir vos propres canaux lorsqu'un incident est ouvert ou mis à jour. Dans l'éditeur de la page, ajoutez des canaux sous **Notifications d'incident**, ou passez `--subscriber <id-du-canal>` (répétable) à `flare status-pages create` ou `update` ; le champ d'API est `subscriberChannelIds`. Chaque mise à jour part sous forme de message avec la page, le statut, le titre de l'incident, votre texte, les composants touchés et un lien vers la page publique. Seuls les canaux webhook (Slack inclus), Telegram, e-mail, Teams et Discord conviennent ; un échec d'envoi est journalisé et ne bloque jamais la publication de la mise à jour. Le lien demande `Alerting__PublicUrl`. Ce sont vos canaux, pas un formulaire d'inscription pour les visiteurs.

### Laisser les visiteurs s'abonner par e-mail

Quand le serveur peut envoyer des e-mails (`Email__Host`, `Email__From`) et connaît son adresse publique (`Alerting__PublicUrl`), la page publique affiche un formulaire **Recevoir les mises à jour par e-mail**. Le visiteur saisit une adresse, reçoit un lien de confirmation, puis, une fois confirmé, reçoit un e-mail pour chaque incident ouvert ou mis à jour sur cette page. Chaque e-mail contient un lien de désabonnement. Sans cette configuration, le formulaire est masqué. Les visiteurs peuvent aussi cocher les composants qui les concernent ; ils ne reçoivent alors que les incidents touchant l'un d'eux, plus tout incident qui ne nomme aucun composant. Une adresse vérifiée ne peut pas être modifiée depuis le formulaire public (sinon n'importe qui connaissant une adresse pourrait restreindre les alertes de cette personne) ; à la place, sur une page de plus d'un composant, chaque e-mail d'incident contient un lien **Choisir les composants qui vous concernent** qui ouvre une page où l'abonné coche les composants et enregistre.

Une adresse non confirmée ne reçoit un nouvel e-mail qu'au plus toutes les 10 minutes, une page garde jusqu'à 2 000 abonnés, et les inscriptions sont limitées à 30 par heure et par adresse appelante (derrière un reverse proxy, c'est l'adresse du proxy). Le formulaire répond de la même façon qu'une adresse soit déjà abonnée ou non. Les administrateurs listent les abonnés d'une page avec `GET /api/status-pages/{id}/subscribers` et en suppriment un avec `DELETE /api/status-pages/{id}/subscribers/{subscriberId}`.

### Personnaliser la page et la servir sur votre propre domaine

Chaque page peut avoir un **logo** (`logoUrl`, une image https), une **couleur d'accent** (`accentColor`, `#rrggbb`), un **lien d'assistance** (`supportUrl`, https ou `mailto:`), affiché comme « Contact support », et un **domaine personnalisé** (`domain`, p. ex. `status.example.com`). Définissez-les dans l'éditeur de page, ou avec `--logo-url`, `--accent-color`, `--support-url` et `--domain` sur `flare status-pages create` et `update` ; une valeur vide efface un champ et l'omettre le conserve.

Flare n'émet pas de certificats et ne gère pas le DNS. Faites pointer le DNS et le TLS de l'hôte (votre reverse proxy ou répartiteur de charge) vers le tableau de bord, puis renseignez le domaine sur la page. Cet hôte ne sert que la page : `/` redirige vers `/status/<slug>`, et tout le reste, y compris le reste du tableau de bord, renvoie 404. N'utilisez pas l'hôte du tableau de bord lui-même comme domaine de page. Chaque domaine appartient à une seule page. Si le tableau de bord joint l'API à une autre adresse que les navigateurs, définissez `API_INTERNAL_URL` sur le tableau de bord. Les changements peuvent mettre jusqu'à une minute à apparaître. Les e-mails d'abonnement pointent toujours vers `Alerting__PublicUrl`.

## À savoir

- La page est publique pour toute personne pouvant joindre l'instance. Dépubliez-la ou supprimez-la pour désactiver le lien immédiatement.
- Le résultat est mis en cache 30 secondes, un changement peut donc mettre ce temps à apparaître. La page se rafraîchit toute seule chaque minute.
- Renommer un moniteur repart de zéro pour son historique, car les résultats de sonde sont stockés sous le nom du moniteur.
- Un composant dont le moniteur ou le SLO a été supprimé reste sur la page avec **No data** ; retirez-le dans l'éditeur.
