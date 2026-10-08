# Comment partager la formulation des notifications entre règles d'alerte

Un modèle de notification est un titre et un corps nommés que plusieurs règles
d'alerte peuvent utiliser. Modifiez-le une fois et toutes les règles qui s'en
servent envoient la nouvelle formulation.

## Créer un modèle

Ouvrez **Paramètres > Modèles de notification** et choisissez **Nouveau
modèle**. Renseignez :

- **Titre** et **Corps (déclenchement)**, avec la même syntaxe
  `{{placeholder}}` que le message propre à une règle (par exemple
  `[{{status}}] {{rule_name}}`). Les espaces réservés inconnus sont refusés à
  l'enregistrement.
- **Corps (résolu)**, facultatif. Utilisé pour les notifications de
  résolution. Vide, il reprend le corps de déclenchement.
- **Corps par canal**, facultatif. Remplacent le corps de déclenchement pour un
  type de canal, par exemple un texte court pour Telegram et un long pour Email.

## L'utiliser sur une règle

Dans le formulaire de la règle, choisissez le modèle sous **Modèle de
notification**. Le sélecteur apparaît dès qu'il existe au moins un modèle. Le
texte saisi sous « Personnaliser le message de notification » prévaut toujours
sur le modèle, champ par champ.

## Définir un modèle par défaut

Activez **Utiliser comme modèle par défaut** pour un seul modèle. Il s'applique
à toutes les règles qui n'en choisissent aucun. Sans modèle par défaut ni modèle
choisi, les règles gardent la formulation intégrée de chaque canal.

## Supprimer un modèle

Un modèle encore utilisé par des règles ne peut pas être supprimé. L'erreur
liste ces règles ; choisissez d'abord un autre modèle pour elles.

## Export et import

`flare alerts export` enregistre le modèle d'une règle par son nom, et `import`
cherche ce nom sur l'instance cible. Créez d'abord le modèle sur celle-ci, sinon
la règle est signalée en erreur.

L'API est `/api/alert-templates`. La conception est décrite dans
l'[ADR-0148](../../docs-internal/adr/0148-shared-alert-notification-templates.md).
