# Recette de stabilisation et pilote

## Installation isolée - à exécuter

Utiliser une VM Windows x64 avec snapshot, un compte administrateur et un compte standard. Ne pas utiliser la machine de travail comme substitut à une installation vierge. L'accès Hyper-V de la session de développement a été refusé ; ces étapes ne sont pas annoncées comme réussies.

| Cas | Procédure | Résultat attendu |
| --- | --- | --- |
| Installation neuve | Construire avec `build/package.ps1 -Version 0.3.1`, installer sans PawnIO | Service Running, ping accepté, application ouverte, capteurs absents explicités |
| Mise à jour | Installer 0.3.0, créer de l'historique, sauvegarder puis installer 0.3.1 | Historique intact, un seul service et un seul Notifier par session |
| Arrêt lent | Faire durer l'arrêt puis lancer la mise à jour | Attente d'état ; échéance signalée, aucune réussite fictive |
| Redémarrage Windows | Redémarrer et ouvrir une session standard | Collecte automatique, Notifier actif, historique relisible |
| Panne initiale | Sur une copie, rendre la base inaccessible, démarrer puis restaurer l'accès | Erreur journalisée, code fatal et récupération SCM vérifiés |
| Interruptions | Veille, arrêt normal, puis arrêt brutal pendant un épisode confirmé | Pas de durée fabriquée ; checkpoint conservé, perte limitée à une minute |
| Matériel | Deux disques de même modèle, puis retrait/remplacement | Identités distinctes ; absence et historique explicités |
| Interface | Rester sur accueil, changer la charge et arrêter la collecte | Actualisation, horodatage et statut périmé visibles sans navigation |
| Notifications | Provoquer une alerte synthétique dans un environnement de test | Toast visible, clic ouvre le bon épisode, reconnexion après redémarrage |
| Accessibilité | Clavier seul, zoom 200 %, thèmes, lecteur d'écran | Navigation et informations essentielles utilisables ; consigner les écarts |
| Désinstallation | Tester conservation puis suppression sur deux snapshots | Choix respecté, service et lancement automatique supprimés |
| Restauration | Restaurer une sauvegarde arrêtée | Historique relisible et collecte reprise |

Enregistrer pour chaque cas : version/empreinte de l'installeur, version Windows, compte, résultat réel, preuve, anomalie éventuelle. Aucun résultat ne doit être prérempli.

## Pilote utilisateur - préparé, non lancé

Après correction des priorités et recette minimale, proposer le pilote à des volontaires avec leur accord. Aucune invitation n'a été envoyée. Utiliser un historique local synthétique pour la première démonstration, puis une collecte réelle sur les seuls PC consentants.

1. Montrer un ralentissement CPU connu, sa chronologie et l'explication ; montrer aussi un capteur indisponible et une période sans relevé.
2. Demander à l'utilisateur d'expliquer ce qu'il comprend et quelle action il entreprendrait. Relever les erreurs d'interprétation.
3. Couvrir l'apprentissage de 14 jours et les créneaux d'usage réellement rencontrés. Consigner les périodes sans utilisation.
4. Annoter les incidents connus, alertes justifiées, alertes jugées inutiles et incidents manqués ; ne pas estimer la précision à partir des seuls événements signalés par le logiciel.
5. Mesurer CPU, mémoire privée, taille SQLite et temps d'affichage sur une machine documentée. Comparer repos, charge et reprise ; fixer les cibles acceptables avec le porteur du projet avant de conclure.

Fiche à remplir : `date;version;machine_anonymisee;scenario;incident_connu;alerte;interpretation;action;cpu_service;memoire_service;taille_donnees;latence_affichage;preuve;decision`.

Décision suivante : corriger les incidents matériels et les mauvaises interprétations, ou élargir progressivement le pilote si la collecte est fiable et les explications comprises. Aucun trafic, revenu, prix ou taux de succès n'est supposé. Préparer une présentation publique après les captures et résultats réels ; ne pas publier une démonstration simulée comme expérience utilisateur observée.

## Parcours 0.4.0 : recette du 23 novembre 2026

Sur une base et un canal IPC de test distincts de l'installation, le paquet autonome a permis de parcourir l'accueil, l'enregistrement et la reprise d'un signalement, son analyse, le rapport d'incident et le bilan de période. L'export PDF A4 produit trois pages sur le jeu de recette ; les pages ont été rendues en PNG et inspectées, et le texte extrait vérifie le masquage du nom du PC et des programmes. Le HTML est testé pour l'échappement, l'inclusion explicite et l'absence de fuite par les phrases d'épisodes historiques.

La chronologie a été contrôlée en plages de 24 heures et d'une heure : graduations, pics, retours à zéro, indication « Sans relevé », curseur partagé et déplacement à la flèche droite. Une activation `sismopc://home` restaure la fenêtre réduite et conserve une seule instance graphique. Les mêmes boutons restent présents après rafraîchissement de l'accueil.

Compléter la recette manuelle avec : écran étroit, thèmes clair et contraste élevé, mise à l'échelle 150/200 %, lecteur d'écran, export vers un dossier refusé et annulation, menu de l'icône, affichage et clic de la notification hebdomadaire, installation et mise à jour sur un Windows propre. Le démarrage du Notifier et le rappel reçu sur le canal ne prouvent pas l'affichage effectif d'un toast par Windows.

Pour un pilote utilisateur, demander sans guider les clics : « votre PC a ralenti il y a une minute ; retrouvez ce qui a été observé, puis préparez un document à partager sans vos noms de programmes ». Observer réussite, hésitations et compréhension de l'incertitude. Aucune amélioration chiffrée d'ergonomie ou de fidélisation n'est revendiquée avant ce pilote.
