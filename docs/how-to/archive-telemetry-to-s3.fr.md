# Archiver la télémétrie vers un stockage compatible S3

Flare peut écrire les heures terminées de logs, de traces et de métriques dans un bucket, en Parquet ou en NDJSON compressé gzip, pour une conservation longue durée ou une analyse avec des outils comme DuckDB, Athena ou Spark. Les données restent aussi dans Flare ; l'archive est une copie. Pour garder les anciennes données *interrogeables* sur un stockage bon marché, voir [Définir la rétention des données](set-data-retention.fr.md).

## L'activer dans le tableau de bord

Créez un bucket (par exemple dans RustFS ou MinIO), puis ouvrez **Paramètres > Espace de travail > Export de télémétrie** (réservé aux administrateurs), remplissez le formulaire **Archive** (URL du bucket, clé d'accès, clé secrète, préfixe, format et signaux), activez **Archive activée** et enregistrez. Le worker d'archivage relit ses paramètres à chaque passage (toutes les cinq minutes par défaut), donc aucun redémarrage n'est nécessaire. Les clés sont masquées une fois enregistrées ; laissez-les telles quelles pour les conserver.

Les paramètres enregistrés **remplacent** la section `Archive` de la configuration du worker en bloc, pas champ par champ, jusqu'à ce que vous choisissiez **Revenir à la configuration**, qui les supprime. Le formulaire indique si l'archive est active et si elle utilise les paramètres enregistrés ou la configuration.

Utilisez un autre bucket que celui du stockage froid.

## Ou l'activer dans des fichiers

Définissez plutôt ces valeurs sur **Flare.AlertWorker**. Avec la pile compose autonome, placez-les dans `.env` :

```bash
FLARE_ARCHIVE_ENABLED=true
FLARE_ARCHIVE_ENDPOINT=http://rustfs:9000/flare-archive   # URL du bucket, style chemin
FLARE_ARCHIVE_ACCESS_KEY=...
FLARE_ARCHIVE_SECRET_KEY=...
```

| Réglage (`Archive__…`) | Défaut | Signification |
|---|---|---|
| `Enabled` | `false` | Interrupteur général. |
| `Endpoint`, `AccessKey`, `SecretKey` | obligatoires | URL du bucket et identifiants. |
| `Prefix` | `flare` | Préfixe de clé dans le bucket. |
| `Format` | `Parquet` | `Parquet` ou `Ndjson` (gzip, un objet JSON par ligne). |
| `Signals__0…` | tous | `Logs`, `Traces`, `Metrics`, au choix. |
| `StartFrom` | heure en cours | Moment d'ingestion de départ au premier lancement, pour rattraper l'historique. |
| `Lag` | `00:10:00` | Délai après la fin d'une heure avant son export. |
| `PollInterval` | `00:05:00` | Fréquence de recherche des heures terminées. |
| `MaxWindowsPerPoll` | `6` | Heures exportées par table et par passage pendant le rattrapage. |

## Suivre sa progression

Le tableau **État de l'export** de la page Export de télémétrie et la carte **Archive** de la page Ingestion (visible par tout utilisateur connecté, affichée seulement tant que l'archive est active) indiquent pour chaque table la dernière heure exportée, le nombre de lignes qu'elle contenait, la date du dernier succès et la dernière erreur, par exemple un bucket inaccessible ou de mauvais identifiants. Une erreur reste visible jusqu'au prochain export réussi ; le worker réessaie à chaque passage.

## Ce qui est écrit

Un objet par table et par heure, organisé pour que les moteurs de requête puissent élaguer par date :

```
flare/logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.parquet
flare/spans/dt=2026-10-08/hh=14/spans-20261008T1400Z.parquet
flare/metrics_gauge/dt=2026-10-08/hh=14/metrics_gauge-20261008T1400Z.parquet
```

Toutes les colonnes sont incluses. Les heures sont déterminées par le moment où Flare a *reçu* les données (`IngestedAt`) : un événement tardif ou antidaté tombe dans l'heure où il est arrivé. Une heure sans données n'écrit aucun objet. Une heure en échec est retentée au passage suivant, et réécrire une heure l'écrase, donc une nouvelle tentative ne duplique jamais les données.

Pour relire avec DuckDB, par exemple : `SELECT * FROM read_parquet('s3://flare-archive/flare/logs/*/*/*.parquet')`.

## Limites

- Les données antérieures à l'activation de l'archive ne sont pas exportées sauf si vous définissez `StartFrom`, et seulement tant qu'elles existent dans Flare.
- Les profils et les tables de configuration de Flare ne sont pas archivés.
- Les identifiants sont envoyés à ClickHouse dans l'instruction d'export ; ils apparaissent donc dans son journal de requêtes.

Notes de conception : [ADR-0156](../../docs-internal/adr/0156-telemetry-archive-s3.md), [ADR-0157](../../docs-internal/adr/0157-managed-telemetry-export.md).
