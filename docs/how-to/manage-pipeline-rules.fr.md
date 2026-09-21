# Comment extraire ou masquer des champs à l'ingestion

Configurez des **règles de pipeline** — extraction et masquage par expression
régulière appliqués aux logs avant leur écriture dans le stockage. Utilisez
l'extraction pour transformer un champ structuré (comme un identifiant
utilisateur ou un code de statut) contenu dans le message d'un log en un
véritable attribut interrogeable. Utilisez le masquage pour occulter des
données sensibles (comme un numéro de carte bancaire ou une adresse e-mail)
afin qu'elles ne soient jamais stockées en clair. Pour la conception derrière
cette fonctionnalité, voir
[ADR-0033](../../docs-internal/adr/0033-pipeline-rules-extraction-redaction.md).

Les règles de pipeline s'exécutent une seule fois, à l'ingestion — avant le
regroupement de motifs de Drain, et avant que quoi que ce soit n'atteigne
ClickHouse. Il n'existe pour l'instant aucun mode d'aperçu/simulation avant
l'enregistrement, donc testez un nouveau motif sur une condition restreinte
d'abord (voir [Cibler une règle](#cibler-une-règle-ne-la-laissez-pas-sans-portée)
ci-dessous) plutôt que sur une règle large couvrant tous vos services.

## Prérequis

- Une instance Flare en cours d'exécution, accessible depuis un navigateur
  (au choix [autonome](run-standalone.fr.md), [Aspire](run-with-aspire.fr.md)
  ou via le [CLI](run-with-cli.fr.md)), avec déjà des données qui arrivent.

## Créer une règle de masquage

1. Ouvrez **Pipeline Rules** dans la navigation supérieure.
2. Cliquez sur **New rule**. Donnez-lui un nom (par ex. « Masquer les
   numéros de carte »).
3. Sous **Applies to**, restreignez éventuellement la règle à certains
   services ou niveaux de log, ou laissez-la sans portée (voir
   [Cibler une règle](#cibler-une-règle-ne-la-laissez-pas-sans-portée)).
4. Sous **Actions**, choisissez **Redact** et saisissez une expression
   régulière à faire correspondre (par ex. `\d{16}` pour un numéro de carte
   à 16 chiffres). Laissez le champ source vide pour masquer `Body`, ou
   saisissez une clé d'attribut pour masquer la valeur de cet attribut à la
   place. Le texte de remplacement est `***` par défaut.
5. Cliquez sur **Create rule**.

À partir de là, tout log correspondant à cette règle voit le texte
correspondant remplacé avant son écriture — y compris le modèle de motif que
calcule la fonctionnalité de regroupement de logs de Drain, de sorte qu'un
corps masqué ne fuite jamais non plus dans un modèle de cluster.

## Créer une règle d'extraction

1. Ouvrez **Pipeline Rules** → **New rule**.
2. Sous **Actions**, choisissez **Extract fields** et saisissez une
   expression régulière avec des **groupes de capture nommés** — le nom du
   groupe devient directement la nouvelle clé d'attribut. Par exemple,
   `user_id=(?<user_id>\d+)` appliqué à un corps du type
   « ...for user_id=42... » ajoute un attribut de log `user_id: "42"`.
3. Cliquez sur **Create rule**.

Les attributs extraits apparaissent comme n'importe quel autre attribut de
log — filtrables dans le Logs Explorer, utilisables comme condition de règle
d'alerte, au même titre qu'un attribut défini directement par l'application
source.

## Cibler une règle (ne la laissez pas sans portée)

Une règle sans filtre de service, de niveau ni de recherche correspond à
**tous** les logs — le formulaire de création/édition affiche alors un
bandeau d'avertissement explicite, afin que ce soit une décision prise
délibérément plutôt qu'un effet accidentel. Préférez cibler le ou les
services spécifiques auxquels un motif est destiné, surtout tant que vous
n'avez pas encore confirmé qu'un nouveau motif se comporte comme prévu.

## Ordre d'exécution, quand plusieurs règles s'appliquent

Les règles s'exécutent dans leur ordre de création, chacune recevant le
résultat de la précédente. Si vous devez extraire un champ du corps
*original* avant qu'une autre règle ne le masque, créez d'abord la règle
d'extraction.

## Désactiver une règle sans la supprimer

Désactivez le bouton **Enabled** dans la boîte de dialogue de création/
édition (la ligne de la règle dans le tableau affiche alors un badge
**Disabled**). Une règle désactivée reste enregistrée mais cesse d'être
appliquée — utile pour mettre une règle en pause pendant une investigation
sans perdre sa configuration.

## Les changements mettent un peu de temps à s'appliquer

Une règle nouvellement créée, modifiée ou désactivée met jusqu'à 30 secondes
à prendre effet — `Flare.Ingest` vérifie les changements de règles selon cet
intervalle plutôt que d'en être notifié instantanément. Cet intervalle est
fixe et n'est pas actuellement configurable depuis le tableau de bord.
