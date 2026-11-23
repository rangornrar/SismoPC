# Évolution produit SismoPC

## Objectif accepté le 23 novembre 2026

Un outil Windows professionnel et immédiatement compréhensible : situation et action utile en premier, signalement d'un ralentissement, diagnostics étayés, courbes lisibles, rapports partageables et présence discrète. Réutiliser WinUI et l'identité turquoise. Conserver les fonctions existantes et les données locales.

## Parcours et critères

- Accueil : situation, action prioritaire, incidents récents et quatre indicateurs ; pas d'inventaire exhaustif ni de grand score.
- Signalement : un clic enregistre l'heure auprès du service, examine les dix minutes précédentes et permet de retrouver l'incident.
- Diagnostic : distinguer observation, contributeur, incertitude et action. Aucun diagnostic automatique de panne sans preuve.
- Rapport : bilan de période ou incident, aperçu avant export PDF/HTML, informations sensibles masquées par défaut, absence de données explicite.
- Courbes : graduations, heures, valeurs au survol, sélection synchronisée et périodes sans relevé identifiées.
- Présence : icône de notification et accès direct ; bilan hebdomadaire facultatif, pas de rappel par défaut.

## État

Implémenté pour la préversion 0.4.0, à partir du commit 16577cd. Compilation Release complète sans avertissement ; 76 tests Core/Data réussis (55 + 21). Paquet autonome des trois programmes produit et démarré dans un dossier de recette séparé.

Parcours observés dans l'application Windows : accueil avec données et avec service indisponible, signalement enregistré et analyse affichée, aperçu général et d'incident, export PDF par boîte Windows, sélection d'une heure dans la chronologie, curseur partagé, indication des trous, déplacement au clavier, réactivation de la fenêtre réduite et instance unique. Les identifiants des boutons de l'accueil sont conservés entre plusieurs rafraîchissements. Le PDF final A4 de trois pages a été rendu et inspecté page par page ; le texte extrait ne contient ni le nom du PC ni les noms de programmes.

Preuves locales non versionnées : `output/evolution-produit/` (captures, PDF, contrôle de confidentialité, données et paquet de test). Les captures initiales sont dans `output/ux-2026-11-23/`. Le contrôle IPC a aussi vérifié la persistance du signalement, la remise à zéro du délai à l’activation du bilan hebdomadaire et la réception d’un rappel échu vers les rapports. Les signalements d'essai et les écritures de réglages concernent exclusivement la base séparée. Le service installé et ses données n'ont pas été modifiés.

Limites de recette : pas d'essais utilisateurs ni de mesure de recommandation/rétention, pas de certification d'accessibilité, thèmes clair/contraste élevé et affichage à forte mise à l'échelle non recettés ; le redimensionnement via l'outil natif n'a pas permis de valider la fenêtre étroite. Le Notifier autonome démarre, mais son menu et l'affichage effectif des toasts Windows restent à recetter avec un utilisateur. L'installation complète sur Windows propre et les démarches Microsoft Store restent séparées de cette préversion.

## Livraison

Canal existant : main et tags GitHub, préversions Windows x64. L'utilisateur a déjà demandé les publications sur ce canal. Ne pas modifier les réglages de collecte ou les données du service installé pour les tests ; utiliser un environnement d'essai séparé.
