# Licences tierces

SismoPC est distribué sous licence MIT. Il utilise les composants suivants :

| Composant | Licence | Usage |
| --- | --- | --- |
| Microsoft.Data.Sqlite, Microsoft.Extensions.*, System.Management, System.Diagnostics.* | MIT | Base de données, hébergement du service, accès Windows |
| SQLite (via SQLitePCLRaw) | Domaine public / Apache-2.0 | Moteur de base de données |
| Serilog, Serilog.Sinks.File | Apache-2.0 | Journaux du service |
| xUnit, Shouldly | Apache-2.0 / BSD-3-Clause | Tests uniquement, non distribués |

À venir : LibreHardwareMonitorLib (MPL-2.0), utilisée sans modification pour lire les capteurs ; sa licence sera jointe à l'installeur.
