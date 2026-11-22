# Architecture et spécifications reconstituées

Ce document décrit les sources 0.3.1. Le besoin historique est reconstitué à partir du README ; les références US/ADR présentes dans les commentaires ne constituent pas un dossier de spécifications fourni.

## Flux

Collecteurs Windows → instantané toutes les 5 secondes → SQLite et détecteurs Core → épisodes et alertes → statut IPC → interface WinUI. Le Notifier s'abonne au pipe pour afficher les événements dans la session. L'interface lit SQLite en mode lecture seule ; les écritures de réglages passent par le service.

## Décisions de stabilisation

- **Mesures inconnues :** une absence de température conserve une alerte existante ; l'absence de tous les disques suspend la résolution de leurs alertes. Le score est masqué quand la surveillance est partielle. Limite : une collecte WMI partiellement réussie ne fournit pas encore un état individuel d'échec pour chaque disque.
- **Historique :** le lecteur combine les résolutions encore présentes pour les petites fenêtres, sans doublonner un agrégat disposant de détail. Les sélections anciennes peuvent afficher une résolution minute ou heure. Le détail processus impose une proximité de 10 secondes.
- **Épisodes :** une rupture de plus de 15 secondes ou une mesure absente clôt l'observation à son dernier point valable. Les épisodes confirmés sont sauvegardés chaque minute. Le modèle n'enregistre pas encore un champ distinct « interruption » ; la dernière date enregistrée reste celle réellement observée.
- **Attribution :** le GPU reste sans responsable plutôt qu'être attribué selon le CPU. La part affichée désigne les programmes relevés. Les compteurs d'E/S processus restent une approximation du disque, pas une mesure physique exhaustive.
- **Identité disque :** série prioritaire, puis UniqueId, puis port. Le libellé incorpore l'identité dans la clé existante pour éviter une migration destructive. Un changement de nom ou de port de repli peut créer une nouvelle identité ; une migration vers une colonne technique indépendante reste à évaluer si cela se produit en recette.
- **Défaillance fatale :** une exception d'initialisation provoque une sortie non nulle, afin que le SCM puisse appliquer sa politique de récupération. Une erreur de tick reste journalisée et la collecte réessaie.

## Vérification

`dotnet test SismoPC.slnx -c Release` valide Core et Data. `dotnet build SismoPC.slnx -c Release` compile également collecteurs, service, Notifier et interface. Les tests ne valident pas les capteurs réels, l'accessibilité, les droits installés, les toasts ni les opérations du SCM. Voir la recette pour ces contrôles.
