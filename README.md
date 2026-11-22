# SismoPC

**SismoPC enregistre en continu l'activité et la santé de ton PC Windows, repère les ralentissements et les anomalies, et explique en clair ce qui s'est passé.**

> Projet en développement (préversion v0.3.1). Licence MIT.

## Ce que fait SismoPC

- **Inventaire** de la configuration : processeur, carte graphique, mémoire, disques, carte mère, BIOS, Windows.
- **Enregistrement continu** par un service Windows léger, même sans session ouverte.
- **Épisodes de ralentissement** : une ressource saturée plus de 30 s, avec le programme responsable.
- **Anomalies** : une mesure qui sort durablement de la normale de *ce* PC (après 14 jours d'apprentissage).
- **Santé du matériel** : usure des SSD, alertes SMART, températures, espace disque.
- **Tendances** : le PC se dégrade-t-il au fil des mois ?
- **Tout reste en local** : aucune donnée envoyée sur internet.

## Organisation du code

| Projet | Rôle |
| --- | --- |
| `src/SismoPC.Core` | Détection des épisodes et anomalies, score de santé, textes d'explication |
| `src/SismoPC.Data` | Base SQLite locale (`C:\ProgramData\SismoPC`) |
| `src/SismoPC.Collectors` | Lecture des compteurs Windows, WMI, températures, santé des disques, journaux |
| `src/SismoPC.Service` | Service Windows de collecte et canal nommé |
| `src/SismoPC.App` | Interface WinUI 3 : accueil, chronologie, épisodes, matériel, tendances, paramètres |
| `src/SismoPC.Notifier` | Notifications Windows dans la session de l'utilisateur |
| `tests/` | Tests automatisés |

## Installer

Télécharge l'installeur de la dernière version dans les [Releases](https://github.com/rangornrar/SismoPC/releases) et lance-le.
L'installeur n'est pas encore signé : Windows SmartScreen peut demander une confirmation (« Informations complémentaires », puis « Exécuter quand même »).
Pour les températures, installe aussi le pilote [PawnIO](https://github.com/namazso/PawnIO.Setup/releases).

## Compiler

Prérequis : SDK .NET 10.

```powershell
dotnet build SismoPC.slnx
dotnet test SismoPC.slnx
```

Chaque envoi sur `main` est compilé et testé par GitHub Actions, qui produit aussi un installeur de développement.
Une version publiée se fait en poussant un tag `vX.Y.Z`, ou en envoyant `main` sur la branche `release` (version lue dans `Directory.Build.props`) : `build/package.ps1` construit l'installeur (Inno Setup) et la release GitHub est créée.

## Licences tierces

Voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).


## Fiabilité et limites

La collecte distingue les capteurs absents : le score est qualifié de surveillance partielle ; une alerte thermique ne disparaît pas faute de mesure. L'accueil se rafraîchit toutes les dix secondes et rejette un statut vieux de plus d'une minute. Les courbes utilisent les résumés disponibles après purge du détail.

Le responsable d'une anomalie GPU reste indéterminé en l'absence de mesure GPU par processus. Les parts attribuées sont relatives aux programmes relevés, pas à l'ensemble de l'activité de Windows. Les épisodes confirmés sont sauvegardés toutes les minutes ; un arrêt brutal peut perdre la dernière minute. Les notifications d'épisode restent émises à la clôture.

Les disques sont distingués par leur numéro de série, leur identifiant unique ou, en dernier recours, leur port. Les anciennes données restent conservées avec leurs noms historiques : une identité perdue dans une ancienne version ne peut pas être reconstruite automatiquement.

Voir [la recette Windows et le pilote](docs/recette-et-pilote.md), [l'exploitation](docs/exploitation.md) et [les décisions d'architecture](docs/architecture.md). La validation en VM et les observations de vrais utilisateurs restent nécessaires avant une version stable. Aucun pourcentage de précision des anomalies n'est établi.
