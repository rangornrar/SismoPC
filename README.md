# SismoPC

**SismoPC enregistre en continu l'activité et la santé de ton PC Windows, repère les ralentissements et les anomalies, et explique en clair ce qui s'est passé.**

> Projet en développement (v0.1). Licence MIT.

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
| `src/SismoPC.Notifier` | Notifications Windows dans la session de l'utilisateur |
| `tests/` | Tests automatisés |

## Compiler

Prérequis : SDK .NET 10.

```powershell
dotnet build SismoPC.slnx
dotnet test SismoPC.slnx
```

Chaque envoi sur `main` est compilé et testé par GitHub Actions.

## Licences tierces

Voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
