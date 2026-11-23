# Architecture et spécifications reconstituées

Ce document décrit les sources 0.4.0. Le besoin historique est reconstitué à partir du README ; les références US/ADR présentes dans les commentaires ne constituent pas un dossier de spécifications fourni.

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


## Correctifs d'affichage 0.3.2

Les écrans sont identifiés par leur InstanceName WMI et conservent leur nom commercial à l'affichage. Le schéma 2 ajoute component_instance sans supprimer les lignes historiques du schéma 1. Le passage à une version antérieure nécessite de restaurer une sauvegarde compatible.

L'accueil conserve son arbre de contrôles et met à jour les valeurs après la lecture asynchrone. Les épisodes utilisent leur identifiant pour conserver le bon lien en cas de changement de liste.

La chronologie affiche à zéro les intervalles sans relevé, sans modifier les valeurs SQLite. Chaque point conserve sa résolution (5 secondes, minute ou heure) pour distinguer un trou d'une cadence agrégée. La réduction graphique conserve minima, maxima et transitions. Un zéro ajouté n'est pas une température réellement mesurée ; l'interface précise que le PC peut être éteint, en veille ou sans collecte. Les interruptions plus courtes qu'un agrégat ancien ne sont pas reconstituables après purge du détail.

## Parcours et rapports 0.4.0

Le signalement manuel passe par `markIncident` sur le canal local : le service fixe l'horodatage et écrit un événement `manual`. L'analyse relit les dix minutes précédentes, avec les résolutions disponibles. Les maxima déterminent des pistes prudentes et des actions ; ils ne démontrent ni durée de saturation ni causalité. Le modèle Core d'analyse et le générateur de rapport ne dépendent pas de WinUI.

Le rapport HTML autonome est partagé entre l'aperçu WebView2, le PDF A4 et l'export HTML. Les entrées sont échappées, les scripts et ressources distantes interdits. Les phrases d'épisodes sont régénérées selon les options pour exclure aussi les noms de programmes des anciennes explications stockées. Les options ne sont pas mémorisées. Le fichier est choisi via `IFileDialog`, boîte Windows rattachée à la fenêtre ; le sélecteur Windows App SDK a provoqué un arrêt natif pendant la recette sur cette machine et n'est pas utilisé dans cette version.

Les courbes conservent la résolution des points, distinguent les zéros ajoutés et réduisent les séries par pixel en gardant le premier, le minimum, le maximum et le dernier point dans l'ordre temporel. Le curseur est déplacé sans reconstruction de la série. La sélection au clavier et la souris partagent le même horodatage entre les courbes.

L'application utilise AppInstance pour rediriger les liens vers la fenêtre déjà ouverte et la restaurer lorsqu'elle est réduite. Le Notifier emploie une boucle Windows Forms pour son icône native ; il continue à recevoir les événements du service par abonnement IPC.

Références consultées le 23 novembre 2026 : [thèmes Windows et couleurs d'accent](https://learn.microsoft.com/en-us/windows/apps/develop/ui/theming), [sélecteur Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/develop/files/pickers-save-file), [IFileDialog](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifiledialog). Le produit conserve l'accent choisi dans Windows pour les contrôles natifs et sa palette turquoise pour les cartes, courbes et rapports. La conformité complète d'accessibilité n'est pas établie par cette recette.
