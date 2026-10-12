# Comment envoyer un tableau de bord par e-mail selon un calendrier

Flare peut rendre un tableau de bord en PDF ou en PNG selon un calendrier et l'envoyer par e-mail, par exemple un rapport hebdomadaire sur les SLO et la latence pour une équipe. Chaque planification a une cadence cron, des destinataires, une plage de temps relative et, en option, des valeurs de variables. Chaque tentative est enregistrée, avec son erreur en cas d'échec.

Les rapports sont rendus par un Chromium sans interface dans `Flare.AlertWorker`, qui ouvre le vrai tableau de bord : un rapport ressemble donc exactement au tableau de bord. La fonction est désactivée par défaut, car le worker a besoin d'un Chromium et d'un serveur SMTP.

## Activer les rapports

Il vous faut un serveur SMTP (les valeurs `SMTP_*` de `.env`, ou les réglages `Email__*` du worker, que les e-mails d'alerte utilisent aussi) et un Chromium pour le worker.

**Docker Compose.** Ajoutez deux lignes à `.env`, puis reconstruisez le worker pour que son image contienne Chromium :

```bash
FLARE_REPORTS_CHROMIUM=true
FLARE_REPORTS_ENABLED=true
```

```bash
docker compose up -d --build alert-worker
```

**Toute autre installation.** Définissez sur le worker d'alertes :

| Réglage | Signification |
| --- | --- |
| `Reports__Enabled` | `true` pour exécuter les planifications. `false` par défaut. |
| `Reports__DashboardUrl` | L'URL du tableau de bord telle que le navigateur l'atteint. Se rabat sur `Alerting__PublicUrl`. |
| `Reports__ApiUrl` | L'URL de l'API telle que le tableau de bord l'atteint, la même valeur que `PUBLIC_API_URL` du tableau de bord. |
| `Reports__ChromiumPath` | Un exécutable Chromium ou Chrome. Inutile dans l'image Compose : elle embarque le Chromium de Playwright. |
| `Reports__BrowserWsEndpoint` | Un serveur Playwright auquel se connecter au lieu de lancer un Chromium local. |
| `Reports__PollInterval` | Fréquence de recherche des planifications échues. 30 secondes par défaut. |
| `Reports__RenderTimeout` | Durée maximale d'un rendu. 2 minutes par défaut. |
| `Reports__SettleDelay` | Attente supplémentaire pour que les graphiques finissent de se dessiner une fois la page calme. 3 secondes par défaut. |
| `Reports__MaxAttachmentBytes` | Taille maximale du fichier envoyé. 20 Mo par défaut. |

## Créer une planification

1. Ouvrez le tableau de bord et choisissez le bouton **Rapports planifiés** (l'icône de calendrier) de sa barre d'outils.
2. Choisissez **Nouvelle planification**.
3. Donnez-lui un nom, choisissez une cadence (ou saisissez une expression cron à cinq champs comme `0 8 * * 1`), un fuseau horaire et les destinataires.
4. Choisissez une plage de temps et un format. **Utiliser la vue actuelle** copie la plage de temps et les valeurs de variables affichées à l'écran.
5. Enregistrez. La planification affiche sa prochaine exécution.

Une planification s'exécute avec vos droits : le rapport contient ce que vous voyez. Si votre compte est désactivé, la planification échoue. Créer, modifier et supprimer des planifications demande le rôle Member ou Admin.

## La tester et lire l'historique

**Envoyer maintenant** met la planification en file d'attente, et le worker l'envoie en une minute environ. **Historique d'exécution** liste chaque tentative avec son statut, sa durée, la taille du fichier et le texte de l'erreur en cas d'échec. Les erreurs courantes :

- *SMTP is not configured* : définissez `Email__Host` et `Email__From` sur le worker.
- *No Chromium to render with* : construisez l'image du worker avec Chromium ou définissez `Reports__ChromiumPath`.
- *Rendering took longer than ...* : augmentez `Reports__RenderTimeout`, ou raccourcissez la plage de temps.
- *The rendered report is ... MB* : utilisez une plage plus courte ou moins de panneaux, ou augmentez `Reports__MaxAttachmentBytes` si votre serveur de messagerie l'accepte.

## Limites

- Un rapport est une seule page haute, plafonnée à 16 000 pixels. Un tableau de bord plus long est coupé à cet endroit.
- Un rapport est une image du tableau de bord à cet instant, pas un export de données. Si un panneau affiche encore un indicateur de chargement dans le fichier, augmentez `Reports__SettleDelay`.
- Un rendu perdu parce que le worker a redémarré n'est pas rejoué. La prochaine échéance cron s'exécute normalement.
- L'e-mail est la seule destination.
