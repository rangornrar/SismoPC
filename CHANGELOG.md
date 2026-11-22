# Changements

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
