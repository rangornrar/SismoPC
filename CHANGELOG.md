# Changements

## 0.4.1

- Profil WebView2 explicitement placé dans `%LocalAppData%\SismoPC\WebView2`, afin que l'aperçu et l'export PDF fonctionnent depuis un dossier d'installation protégé comme Program Files.
- Message distinct en cas de refus d'accès, code technique local et bouton pour réessayer l'aperçu ; l'absence de runtime n'est plus présumée pour toutes les erreurs.
- Diagnostic minimal par utilisateur dans `%LocalAppData%\SismoPC\logs\preview.log`, sans contenu de rapport.

## 0.4.0

- Accueil centré sur la situation, l'action utile, les incidents récents et quatre mesures compactes.
- Signalement manuel d'un ralentissement, sauvegardé par le service et réexaminable depuis l'accueil ou la chronologie ; analyse des dix minutes précédentes.
- Observations, actions et incertitudes séparées ; vocabulaire de contribution plutôt que causalité affirmée.
- Rapports locaux de période ou d'incident, aperçu WebView2, export PDF/HTML et informations sensibles exclues par défaut.
- Courbes graduées, valeurs au survol, curseur partagé et sélection au clavier.
- Icône de notification avec accès directs ; bilan hebdomadaire sur option, instance unique de l'interface et réactivation des liens.
- État actuel des disques fondé sur le dernier relevé, sans afficher les anciennes identités comme des composants actuels.
- Recette séparée via un dossier de données et un canal IPC spécifiques, sans modification de l'installation existante.

## 0.3.2

- Écrans de même modèle conservés séparément grâce à leur identité WMI ; migration additive de l'inventaire.
- Rafraîchissement de l'accueil sans effacement intermédiaire ni reconstruction des cartes à chaque lecture.
- Chronologie à zéro pendant les périodes sans relevé, avec prise en compte de la résolution et conservation des transitions dans le rendu.
- Validation : trois écrans remontés par Windows (dont deux PL2530H), 68 tests automatisés réussis et compilation Release réussie avant changement de version.

## 0.3.1

- Conservation des alertes lorsque les températures deviennent inconnues ; état de surveillance partielle et score masqué.
- Rafraîchissement et horodatage de l'accueil, rejet des statuts périmés.
- Consultation des agrégats après expiration du détail et contrôle de proximité des relevés de programmes.
- Gestion des interruptions de collecte, clôture des anomalies et checkpoints des épisodes confirmés.
- Attribution GPU sans recours au poids CPU ; explications plus précises et alertes actualisées.
- Distinction des disques par série, identifiant ou port, avec conservation de l'ancien historique.
- Limites de taille, de durée et de concurrence des échanges IPC.
- Contrôles d'arrêt et de disponibilité du service dans l'installeur.
- Guides d'architecture, d'exploitation, de recette et de pilote.

Validation locale : compilation Release et 59 tests réussis. La recette complète du produit installé, les capteurs réels, les notifications et le pilote utilisateur restent à effectuer. Cette version conserve le statut de préversion.
