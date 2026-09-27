# Comment trouver où les requêtes décrochent avec les entonnoirs de traces

Un entonnoir de traces suit les requêtes à travers une liste ordonnée
d'étapes, par exemple checkout → paiement → confirmation. Pour chaque étape,
il indique combien de traces l'ont atteinte, combien ont décroché avant
l'étape suivante, combien y ont échoué, et combien de temps a pris le passage
depuis l'étape précédente.

Les entonnoirs utilisent les spans que Flare stocke déjà. Vous n'avez pas à
modifier votre instrumentation, et un entonnoir fonctionne sur des spans
stockés avant sa définition.

## Prérequis

- Une instance Flare en fonctionnement qui reçoit les traces de vos
  applications.
- Les étapes à suivre doivent se produire dans **une seule trace**. Les
  services concernés doivent se transmettre le contexte de trace, ce que font
  par défaut les instrumentations OpenTelemetry HTTP, gRPC et de messagerie.

## Définir les étapes

1. Ouvrez **Traces** dans la barre de navigation, puis l'onglet **Funnels**.
2. Pour chaque étape, renseignez au choix :
   - **Service** : le `service.name` exact du span.
   - **Nom de span** : le nom exact du span (l'opération), par exemple
     `POST /checkout`.
   - Des filtres **Attribute** : un attribut de span, de ressource ou de
     scope, avec les mêmes opérateurs que l'explorateur de traces (égal,
     regex, un parmi, existe, etc.).

   Un span correspond à l'étape quand toutes ses conditions sont remplies.
   Chaque étape a besoin d'au moins une condition. Les deux sélecteurs
   proposent les valeurs vues dans la fenêtre choisie.
3. Utilisez **Add step** pour aller jusqu'à six étapes, et les flèches pour
   les réordonner.
4. Choisissez une fenêtre (de 5 minutes à 24 heures) et cliquez sur **Run**.

## Lire les résultats

| Colonne | Signification |
|---|---|
| Traces | Traces ayant atteint cette étape et toutes les précédentes, dans l'ordre, avec leur part parmi celles entrées à l'étape 1 |
| From previous | Part des traces de l'étape précédente qui ont atteint celle-ci |
| Dropped after | Traces ayant atteint cette étape mais pas la suivante |
| Errors | Traces dont le span de cette étape a un statut d'erreur |
| p50 / p95 transition | Temps entre le début du span de l'étape précédente et le début du span de cette étape. Survolez pour voir la moyenne et le p99 |

Cliquez sur un nombre sous **Traces**, **Dropped after** ou **Errors** pour
lister ces traces, des plus récentes aux plus anciennes (jusqu'à 100).
Cliquez sur un identifiant de trace pour ouvrir sa cascade.

L'entonnoir ne se relance pas pendant que vous modifiez les étapes. Cliquez à
nouveau sur **Run** après les avoir changées. Changer la fenêtre le relance.

### Comment les étapes sont appariées

Une trace entre dans l'entonnoir avec son premier span correspondant à
l'étape 1. Chaque étape suivante utilise ensuite le premier span
correspondant qui commence au même moment que le span de l'étape précédente
ou après :

- Une étape qui ne s'est produite *qu'avant* la précédente ne compte pas.
- Quand plusieurs spans correspondent après l'étape précédente, le premier
  compte. Si un paiement échoue puis qu'une nouvelle tentative réussit, la
  trace atteint l'étape de paiement et y compte comme une erreur, et la
  latence est mesurée jusqu'à la tentative échouée.
- Un même span ne peut pas satisfaire deux étapes consécutives.

Seuls les spans qui commencent dans la fenêtre sont pris en compte. Une trace
encore en cours à la fin de la fenêtre peut apparaître comme ayant décroché.

## Enregistrer et partager un entonnoir

Utilisez **Views** → **Save current view** pour enregistrer les étapes et la
fenêtre. Les entonnoirs enregistrés apparaissent dans le menu Views de cette
page et sur la page **Views**. Leur lien **Copy shareable link** ouvre
l'entonnoir et le lance. La page rouvre le dernier entonnoir choisi.

## Dépannage

**Aucune trace n'a atteint l'étape 1.** Comparez le service et le nom de span
avec une trace dans l'explorateur de traces. Les deux doivent correspondre
exactement, casse comprise.

**Tout décroche à une étape, alors que les traces semblent complètes.** Les
deux services démarrent peut-être des traces distinctes au lieu d'une seule.
Ouvrez une trace depuis la liste détaillée et vérifiez que le span de l'étape
suivante s'y trouve.

**L'entonnoir est lent sur une longue fenêtre.** Un entonnoir regroupe tous
les spans de la fenêtre qui correspondent à une étape. Indiquez un service à
chaque étape pour que Flare ignore les données des autres services, ou
utilisez une fenêtre plus courte.
