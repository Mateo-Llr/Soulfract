# Soulfract

## Sommaire

- [Changelog](#changelog)
  - [Avril 2026](#avril-2026)
  - [Mai 2026](#mai-2026)
  - [Juin 2026](#juin-2026)
  - [Juillet 2026](#juillet-2026)
  - [Août 2026](#aout-2026)
  - [Septembre 2026](#septembre-2026)
- [Potentiels ajouts futurs](#potentiels-ajouts-futurs)
  - [Items](#items)
  - [Features](#features)
  - [Fixs](#fixs)

---

# Changelog

## Avril 2026

### 14 avril

- [CHANGED] Refonte complète du système de lumière dynamique
- [REMOVED] Suppression de l'ancien système de lumière
- [ADDED] Ajout des premiers menus du jeu
- [CHANGED] Unification des systèmes de rendu
- [FIXED] Correction du comportement des animaux qui convergeaient vers le centre de la carte
- [CHANGED] Modification de la gestion de l'altitude avec les demi-tuiles

### 15 avril

- [CHANGED] Prise en compte des dimensions réelles des textures dans le code
- [FIXED] Correction de l'affichage des falaises
- [ADDED] Ajout d'arbres avec yeux et bouche dans les biomes
- [FIXED] Correction du déplacement des animaux
- [FIXED] Correction de la parallaxe du menu
- [FIXED] Correction de la fermeture du jeu avec Echap
- [CHANGED] Modification du comportement de la touche Echap
- [CHANGED] Mise à jour du menu pause et de son titre
- [FIXED] Correction des ambiguïtés de constructeurs `Color` en C#
- [ADDED] Ajout d'un système de voitures avec textures empilées
- [CHANGED] Mise à jour du rendu des voitures
- [ADDED] Ajout d'un rendu 3D par empilement de textures pour les voitures
- [FIXED] Correction de l'orientation des voitures
- [CHANGED] Amélioration de la rotation des roues
- [PERF] Optimisation du chargement des entités dans les chunks déchargés
- [CHANGED] Masquage des jambes des entités lorsqu'elles sont dans l'eau
- [FIXED] Correction des bordures de biomes lors des rotations
- [FIXED] Correction du placement et du rendu des objets
- [PERF] Optimisation des performances du rendu de l'eau
- [FIXED] Correction de plusieurs erreurs de compilation C#
- [ADDED] Ajout d'une seed aléatoire pour la génération du monde

### 16 avril

- [CHANGED] Mise à jour des biomes forêt et savane
- [FIXED] Correction de la caméra et du placement en hauteur sur le terrain
- [FIXED] Correction du placement fantôme des tuiles
- [FIXED] Correction du placement des tuiles
- [CHANGED] Amélioration des interactions entre les tuiles et leurs niveaux d'altitude
- [ADDED] Échange d'objets directement dans l'inventaire
- [ADDED] Sauvegarde multi-parties
- [ADDED] Fond défilant dans le menu de création du monde
- [FIXED] Correction du fond du menu de création du monde
- [FIXED] Correction de la sélection des mondes
- [FIXED] Correction de la création des mondes

### 17 avril

- [PERF] Optimisation du chargement des chunks
- [ADDED] Ajout du système d'équipements
- [ADDED] Ajout du système de craft
- [CHANGED] Catégorisation des couvre-chefs
- [FIXED] Correction des interactions clavier lorsque le chat est ouvert
- [ADDED] L'objet tenu en main est attaché au bras du personnage
- [ADDED] Ajout des grottes et d'un monde souterrain
- [ADDED] Ajout d'une touche P pour entrer dans le système de grottes
- [ADDED] Sauvegarde indépendante des mondes de surface et des grottes
- [FIXED] Correction de l'IA d'errance aléatoire des animaux
- [ADDED] Ajout d'entrées naturelles aux grottes
- [CHANGED] Séparation des objets placés entre surface et grottes
- [ADDED] Ajout des dégâts flottants
- [ADDED] Ajout des connexions entre tuiles connectables
- [FIXED] Correction de la collision entre clôtures et cactus

### 19 avril

- [ADDED] Génération d'une arborescence de fichiers JSON pour les données du jeu
- [CHANGED] Amélioration de l'organisation du code C# du jeu Raylib
- [FIXED] Correction du flash visuel des objets lorsqu'ils prennent des dégâts
- [CHANGED] Amélioration du contour blanc des tuiles
- [FIXED] Correction du hover des objets collectables multi-tuiles
- [ADDED] Ajout de la musique jouée par les instruments
- [CHANGED] Mise en place de la lecture MIDI pour les instruments

### 20 avril

- [ADDED] Ajout d'un système musical avec guitare
- [FIXED] Correction des avertissements du code C#
- [FIXED] Correction du filtre jaunâtre appliqué aux entités
- [FIXED] Correction des hitbox des animaux
- [FIXED] Correction de la hitbox des attaques
- [CHANGED] Refactorisation du code devenu trop long
- [FIXED] Correction d'un plantage lié aux textures du menu
- [CHANGED] Modification de l'angle du bras lors du port d'un objet
- [CHANGED] Masquage des objets plaçables lorsqu'ils sont tenus en main
- [CHANGED] Refonte du menu d'inventaire et de craft
- [CHANGED] Regroupement des notifications d'objets
- [CHANGED] Amélioration de la génération des montagnes
- [CHANGED] Amélioration de la génération des montagnes et de la neige
- [ADDED] Génération de terrains avec montagnes
- [FIXED] Correction de la récupération des objets cassés
- [FIXED] Correction de l'apparition des biomes océaniques
- [ADDED] Implémentation de la nage des entités
- [ADDED] Ajout de l'icône et des métadonnées Discord
- [ADDED] Ajout d'une IA de combat pour les animaux sauvages
- [ADDED] Ajout du tir à l'arc
- [PERF] Optimisation du code C# Raylib
- [CHANGED] Amélioration du rendu des voitures avec ombrage vertical

### 21 avril

- [ADDED] Ajout des flèches
- [PERF] Optimisation générale des performances du jeu
- [ADDED] Possibilité d'attirer les animaux avec de la nourriture
- [ADDED] Animation du swing du bras droit lors des attaques
- [CHANGED] Amélioration de l'animation du bras droit
- [ADDED] Ajout de textures pour les équipements tenus en main
- [CHANGED] Refonte de l'interface d'inventaire avec onglets
- [ADDED] Ajout du système jour/nuit
- [ADDED] Ajout du système de lumière associé au cycle jour/nuit
- [FIXED] Correction du rendu de la lumière
- [FIXED] Correction de l'effet « donut » autour des sources lumineuses
- [FIXED] Correction du rendu des lumières
- [FIXED] Correction de la taille des lumières lors du zoom
- [FIXED] Correction du centre non éclairé des sources lumineuses
- [ADDED] Animation des tuiles
- [FIXED] Correction de l'animation des torches
- [FIXED] Correction des variations de textures

### 22 avril

- [ADDED] Sauvegarde de l'heure du jeu
- [ADDED] Ajout des ciseaux permettant de changer de coiffure
- [ADDED] Ajout du système de pêche
- [ADDED] Configuration des objets lumineux
- [ADDED] Apparition aléatoire du boss Ogre
- [CHANGED] Génération des grottes avec murs de roche
- [FIXED] Correction des interactions avec la surface depuis les grottes
- [FIXED] Correction des hitbox des tuiles cassables
- [CHANGED] Suppression de la lumière du jour dans les grottes
- [CHANGED] Modification de la génération des grottes avec des Rock Blocks
- [ADDED] Ajout d'une icône de fenêtre au jeu Raylib
- [CHANGED] Amélioration de l'affichage des détails de craft
- [FIXED] Correction d'une `AccessViolationException` dans le menu principal
- [ADDED] Ajout d'une porte-armure interactive
- [ADDED] Ajout de tuiles de sol modifiables
- [FIXED] Correction de l'affichage des tuiles de sol modifiées
- [CHANGED] Modification du système de tuiles de sol
- [FIXED] Correction de la hitbox des attaques contre les tuiles
- [CHANGED] Passage du double-clic au simple clic pour attaquer
- [ADDED] Possibilité de casser les tuiles
- [FIXED] Correction des interactions avec les tuiles aux coordonnées zéro
- [ADDED] Ajout des bordures de biomes
- [FIXED] Correction des priorités des bordures de biomes
- [ADDED] Ajout de particules de fumée et de braises aux torches et feux de camp
- [ADDED] Sauvegarde de l'état des grottes
- [ADDED] Possibilité de manger avec le clic droit
- [FIXED] Correction du chargement des textures d'équipement
- [ADDED] Ajout du système de cultures avec croissance
- [ADDED] Ajout de coffres craftables avec inventaire

### 23 avril

- [FIXED] Correction des couleurs de peau étranges
- [ADDED] Ajout de bateaux et de radeaux
- [ADDED] Ajout d'un effet de secousse de l'écran lors des dégâts
- [ADDED] Ajout du système de drops au sol
- [ADDED] Attraction des drops vers le joueur
- [CHANGED] Amélioration des interactions avec les tuiles et leurs icônes
- [ADDED] Ajout du portage des objets déplaçables
- [CHANGED] Amélioration du système de troupeaux
- [ADDED] Ajout de l'apprivoisement des animaux
- [CHANGED] Amélioration du comportement IA des animaux apprivoisés
- [CHANGED] Amélioration de l'interface des animaux apprivoisés
- [CHANGED] Refonte de l'interface avec textures
- [CHANGED] Ancrage en bas des grandes textures
- [CHANGED] Amélioration du système de toits
- [ADDED] Ajout automatique de toits sur les murs en brique
- [CHANGED] Masquage des toits à l'intérieur
- [ADDED] Gestion des toitures pour les maisons complexes
- [CHANGED] Les portes sont désormais considérées dans la détection des murs
- [CHANGED] Regroupement des maisons connectées pour gérer leurs intérieurs
- [CHANGED] Agrandissement des biomes
- [FIXED] Correction du spawn des arbres dans les déserts
- [CHANGED] Refonte de la génération des biomes
- [FIXED] Correction du spawn du joueur dans les plaines

### 24 avril

- [FIXED] Correction de la position des grottes
- [ADDED] Ajout de veines de minerais dans les grottes
- [ADDED] Ajout de lumière provenant de l'objet équipé par le joueur
- [FIXED] Correction de l'animation des jambes de l'araignée
- [FIXED] Correction d'une division par zéro dans les animations
- [CHANGED] Amélioration du rendu des intérieurs de maisons
- [CHANGED] Masquage des murs à l'intérieur des maisons
- [FIXED] Correction du rendu des toits
- [FIXED] Correction du rendu des grands toits
- [ADDED] Reconstruction des toits après chargement d'une partie
- [CHANGED] Amélioration de l'interface des conteneurs modulables
- [ADDED] Déplacement des meubles
- [CHANGED] Modification de la connexion des étagères
- [FIXED] Correction d'une erreur de sauvegarde de chunk `null`
- [FIXED] Correction de la sauvegarde des étagères
- [CHANGED] Modification de la pose de la porte-armure
- [ADDED] Affichage de plusieurs interactions sur une même tuile
- [CHANGED] Différenciation des icônes d'interaction des objets
- [CHANGED] Modification de l'affichage des interactions sur les tuiles
- [ADDED] Possibilité de s'asseoir sur les toilettes
- [FIXED] Correction de l'animation assise
- [CHANGED] Ouverture de l'inventaire directement sur l'onglet artisanat
- [CHANGED] Amélioration de l'IA de combat du boss Ogre
- [CHANGED] Tir à l'arc avec clic droit
- [FIXED] Correction du calcul des interactions selon la hauteur des tuiles
- [FIXED] Correction des hitbox des objets multi-tuiles

### 25 avril

- [ADDED] Sauvegarde des structures générées
- [FIXED] Correction de la génération des villages et des routes
- [ADDED] Ajout des commerçants PNJ
- [FIXED] Correction du crash des PNJ commerçants
- [FIXED] Correction de la fermeture de l'interface commerçant
- [CHANGED] Utilisation de textures redimensionnables pour les interfaces
- [FIXED] Correction des interactions avec les stations
- [CHANGED] Remplacement du menu de craft par une interface unifiée
- [CHANGED] Refonte de l'interface inventaire/artisanat
- [FIXED] Correction de l'affichage du menu craft en 9-slice
- [FIXED] Blocage des clics traversant les interfaces
- [ADDED] Interface de coffre déplaçable avec l'inventaire

### 26 avril

- [FIXED] Correction de la perte d'objets lors des échanges avec les conteneurs et porte-armures
- [ADDED] Ajout d'un effet visuel de blessure avec battement de cœur
- [FIXED] Correction de la disparition d'objets de l'inventaire
- [ADDED] Mise en place d'un système d'animation flexible
- [CHANGED] Amélioration de la flexibilité du système d'animations
- [ADDED] Chargement automatique des animations
- [FIXED] Correction de la caméra lors de l'assise sur les toilettes
- [FIXED] Correction de l'affichage du personnage assis derrière les éléments
- [ADDED] Ajout de revêtements muraux personnalisables
- [FIXED] Correction de la gestion des objets de l'inventaire
- [CHANGED] Modification du scrolling de la hotbar
- [ADDED] Mise à jour des dynamites
- [PERF] Optimisation du lag lors des dézoomages importants
- [ADDED] Ajout de raccourcis clavier pour les interfaces
- [CHANGED] Amélioration du rendu de la lumière avec occlusion par les murs
- [CHANGED] Amélioration du rendu des ombres
- [CHANGED] Modification du système d'équipement avec `armorSlot`
- [ADDED] Craft de colorants à partir des fleurs
- [ADDED] Ajout d'un bac à teinture
- [ADDED] Ajout du système d'étages
- [REMOVED] Suppression du spawn naturel du boss
- [CHANGED] Modification de l'ID des flèches
- [CHANGED] Modification du système de visée et de tir

### 27 avril

- [FIXED] Correction de la visée à l'arc
- [ADDED] Ajout du cassage des amas de pierre donnant des ressources rares
- [ADDED] Superposition du pantalon sur le torse
- [CHANGED] Masquage des connexions intérieures
- [REMOVED] Suppression des hauteurs dans les grottes
- [ADDED] Ajout du système de succès

### 28 avril

- [ADDED] Ajout du chaudron interactif
- [FIXED] Correction de la texture overlay de l'item 63

### 29 avril

- [ADDED] Ajout de la teinture des potions
- [CHANGED] Modification de `ItemRenderer` pour gérer les couleurs
- [CHANGED] Amélioration du rendu des voitures avec contour noir
- [PERF] Optimisation de `World.cs`
- [REMOVED] Suppression de l'assombrissement de l'écran pendant la pause
- [FIXED] Blocage des attaques lorsqu'une interface est ouverte

### 30 avril

- [FIXED] Correction de l'interface du chaudron
- [FIXED] Blocage des interactions souris traversant le chaudron

## Mai 2026

### 1er mai

- [ADDED] Repousse naturelle des objets collectables
- [ADDED] Possibilité de jouer en multijoueur en ligne
- [CHANGED] Remplacement de la police globale par `pixel_font.ttf`
- [CHANGED] Refonte de la génération du monde avec des paliers
- [FIXED] Correction de l'affichage des tuiles hautes
- [ADDED] Génération de terrains en plateaux et montagnes
- [FIXED] Correction du Y-sorting des entités et tuiles avec élévation
- [FIXED] Correction de la synchronisation des particules Zzz
- [CHANGED] Refonte du système de pêche, sur le modèle de la dynamite

### 2 mai

- [ADDED] Ajout d'un aquarium avec poissons
- [ADDED] Ajout de l'animation des poissons de l'aquarium
- [CHANGED] Ajout d'un mouvement fluide de caméra
- [ADDED] Possibilité de créer un chemin avec une pelle
- [ADDED] Ajout du cercle rituel avec bougies
- [CHANGED] Refonte de l'affichage du cercle rituel
- [FIXED] Correction du placement du cercle rituel
- [FIXED] Correction du décalage du cercle satanique
- [CHANGED] Refonte de l'interface du cercle rituel
- [FIXED] Correction de l'interaction avec le cercle rituel
- [CHANGED] Suppression du blocage du bras lors du placement d'objets
- [ADDED] Ajout d'un menu dédié au cercle rituel
- [FIXED] Correction de l'affichage des accents
- [FIXED] Correction de la minimap pour afficher le véritable sol
- [FIXED] Correction des toits bas dans les villages générés
- [CHANGED] Harmonisation de l'altitude du terrain
- [CHANGED] Passage du clavier de création du monde en AZERTY
- [FIXED] Correction des humains figés dans les portes des maisons
- [ADDED] Carte d'exploration affichant les zones visitées
- [ADDED] Ajout de baguettes magiques et de boules de feu
- [ADDED] Ajout de lumière aux boules de feu
- [ADDED] Les boules de feu suivent le curseur
- [ADDED] Ajout de donjons aléatoires dans les grottes

### 3 mai

- [CHANGED] Génération anticipée des donjons
- [FIXED] Évitement du spawn des donjons directement sur le joueur
- [CHANGED] Amélioration du spawn des donjons pour éviter les superpositions
- [FIXED] Correction de la génération des donjons
- [ADDED] Ajout d'un menu de téléportation vers les pylônes
- [CHANGED] Modification du système d'équipement de l'inventaire
- [FIXED] Correction de l'affichage des accents
- [FIXED] Correction de la police du chat
- [ADDED] Attraction des objets avec la touche E
- [ADDED] Ajout de l'animation de course du joueur

### 4 mai

- [FIXED] Correction de la conservation du contenu des sacs à dos

### 5 mai

- [ADDED] Ajout de la liste des contrôles du jeu
- [CHANGED] Fermeture du menu pause avec Echap
- [FIXED] Correction du comportement d'Echap dans le menu pause
- [ADDED] Ajout d'un miroir pour personnaliser le personnage
- [ADDED] Personnalisation du personnage via le miroir
- [FIXED] Correction de l'interface du miroir
- [CHANGED] Modification de l'aperçu du personnage dans le miroir
- [CHANGED] Amélioration du color picker

### 6 mai

- [PERF] Optimisation importante des FPS
- [CHANGED] Amélioration du système de lumière avec occlusion
- [PERF] Optimisation de `Program.cs`
- [ADDED] Ajout d'herbe animée par le vent et les interactions des entités
- [CHANGED] Harmonisation des hauteurs entre les biomes
- [FIXED] Correction de l'extension des radeaux après déplacement

### 9 mai

- [ADDED] Ajout de nuages procéduraux avec ombres
- [FIXED] Correction de l'affichage des nuages
- [PERF] Optimisation des performances générales
- [CHANGED] Refonte du système de bateaux autour d'une tilemap interne propre à chaque bateau
- [CHANGED] Amélioration du rendu des bateaux pour éviter les duplications
- [ADDED] Possibilité de placer des objets sur les bateaux
- [FIXED] Correction des connexions entre radeaux et bateaux

### 10 mai

- [ADDED] Ajout d'un effet visuel autour des objets dans l'eau
- [CHANGED] Remplacement de l'ombre des entités et objets dans l'eau par un effet d'immersion
- [FIXED] Correction de la hitbox des bateaux
- [ADDED] Possibilité de porter les tuiles sur les bateaux
- [FIXED] Correction des interactions avec les objets sur les bateaux
- [ADDED] Sauvegarde des objets placés sur les bateaux
- [CHANGED] Tri des objets, entités et éléments sur les bateaux
- [ADDED] Rechargement des ressources avec F1
- [FIXED] Correction du rendu du joueur
- [CHANGED] Ajout d'un nouveau rendu des bateaux
- [ADDED] Ombres des nuages avec déplacement doux
- [PERF] Optimisation du code du jeu
- [CHANGED] Génération du monde basée sur l'altitude
- [ADDED] Ajout d'un système de plongée sous-marine
- [ADDED] Création d'un monde sous-marin parallèle
- [ADDED] Physique de nage sous l'eau
- [ADDED] Génération d'un fond marin
- [CHANGED] Ajustement de la caméra selon la profondeur
- [FIXED] Correction des déplacements verticaux sous l'eau
- [CHANGED] Modification de la remontée à la surface
- [FIXED] Correction du masquage des jambes des entités sous l'eau
- [CHANGED] Équilibrage de la génération des océans et montagnes

### 11 mai

- [PERF] Optimisation du chargement des chunks
- [CHANGED] Ajout d'une icône dans la barre des tâches
- [FIXED] Correction de plusieurs problèmes de fonctionnement du jeu

### 14 mai

- [PERF] Optimisation du rendu lors des dézoomages
- [FIXED] Correction du comportement des loups et ours neutres lors des attaques
- [CHANGED] Amélioration du rendu de l'eau avec shaders
- [ADDED] Mise en place d'un shader d'eau global

### 15 mai

- [ADDED] Couleur de l'eau dynamique selon la profondeur et la température
- [ADDED] Ajout d'une interface de voile pour les bateaux
- [ADDED] Interaction avec la voile des bateaux
- [REMOVED] Suppression temporaire des fonctionnalités liées aux voiles
- [CHANGED] Refonte du système de lumière avec ombres

### 30 mai

- [CHANGED] Amélioration des bulles de notification
- [FIXED] Correction d'un overflow dans `World.DrawWorld`

## Juin 2026

### 2 juin

- [FIXED] Correction de la conservation du contenu des conteneurs portables

### 3 juin

- [FIXED] Correction de la persistance des conteneurs portables et des sacs à dos
- [CHANGED] Les sacs à dos disposent désormais d'un stockage individuel

### 10 juin

- [CHANGED] Refonte du système de stockage de l'inventaire
- [FIXED] Correction de l'inventaire et du craft des sacs à dos
- [ADDED] Affichage du sac à dos sur le personnage
- [ADDED] Affichage du sac à dos lorsqu'il est équipé
- [ADDED] Ajout de l'ère de la vapeur avec énergie et fils

### 11 juin

- [ADDED] Ajout des ampoules électriques dans le système énergétique
- [FIXED] Correction de la dynamo de la machine à vapeur

### 12 juin

- [FIXED] Correction de la production d'énergie par la dynamo
- [ADDED] Sauvegarde complète des systèmes énergétiques
- [ADDED] Ajout d'infobulles aux machines à vapeur
- [CHANGED] Amélioration des infobulles des machines à vapeur
- [FIXED] Correction de la lumière des ampoules électriques
- [FIXED] Correction de la sauvegarde des machines à vapeur
- [ADDED] Ajout du filet pour attraper les papillons et insectes volants
- [CHANGED] Remplacement de l'interface du coffre par une texture bois

### 13 juin

- [FIXED] Correction de l'interaction avec le panier via E
- [FIXED] Correction des particules de dégâts flottants
- [CHANGED] Décalage de l'affichage du sac à dos derrière le corps
- [PERF] Optimisation des caches du code
- [ADDED] Ajout de la teinture multicouche des équipements
- [CHANGED] Migration des vêtements vers la gestion de couleurs multiples
- [FIXED] Correction du système de teintes multiples

### 14 juin

- [CHANGED] Mise à jour de la gestion des teintures multiples

### 17 juin

- [ADDED] Ajout du menu d'items du godmode
- [ADDED] Ajout de `ItemMenuUI`
- [CHANGED] Amélioration de l'interface du menu d'items
- [CHANGED] Amélioration du mouvement des libellules

### 18 juin

- [ADDED] Implémentation d'un lecteur MIDI
- [CHANGED] Correction et gestion du tempo musical
- [ADDED] Définition des instruments MIDI dans les fichiers JSON
- [CHANGED] Refonte du système de lecture musicale
- [PERF] Optimisation du système musical
- [CHANGED] Amélioration de la lumière pixel-perfect
- [ADDED] Ajout de `CraftingUI`
- [FIXED] Correction des quantités dans `CraftingUI`
- [ADDED] Notifications indiquant la rareté des objets
- [FIXED] Correction de `CraftUI`
- [ADDED] Ajout du Shift+clic
- [ADDED] Ajout de tables de craft
- [FIXED] Blocage des clics traversant les menus

### 19 juin

- [CHANGED] Refonte du système de combat
- [CHANGED] Limitation de la distance d'attaque à trois blocs
- [ADDED] Ajout des hitframes
- [CHANGED] Mise à jour du code de combat Vendetta

### 21 juin

- [ADDED] Ajout d'un cooldown d'attaque
- [CHANGED] Amélioration de l'animation de swing
- [CHANGED] Ajout de physique au mouvement du bras pendant le swing
- [REMOVED] Remplacement de la hotbar classique par une roue
- [ADDED] Ajout du `RadialMenu`

### 22 juin

- [ADDED] Génération des donjons à partir d'une seed
- [REMOVED] Retrait des blocs de grotte indésirables
- [CHANGED] Modification de l'affichage des tuiles
- [CHANGED] Ajout d'un shader d'eau réaliste
- [CHANGED] Refonte du système de craft

### 23 juin

- [CHANGED] Amélioration du système de toits
- [ADDED] Ajout de l'herbe comme objet décoratif
- [ADDED] Ajout des bordures de biomes
- [ADDED] Arrosage des cultures
- [ADDED] Sauvegarde de l'humidité des cultures
- [FIXED] Correction de l'assèchement du sol des cultures

### 24 juin

- [FIXED] Correction de la sauvegarde des plantes
- [ADDED] Ajout d'une bordure aux farmland

### 25 juin

- [ADDED] Ajout du suivi des entités à la queue leu leu
- [PERF] Optimisation du système de sauvegarde

### 26 juin

- [FIXED] Correction de plusieurs erreurs de compilation liées aux types manquants
- [ADDED] Rechargement des données avec F4
- [CHANGED] Amélioration de l'ordre de rendu et des transitions d'animations
- [FIXED] Correction du rendu des pattes des animaux
- [ADDED] Animation de course
- [CHANGED] Amélioration du chat
- [FIXED] Correction du chat qui restait affiché lorsqu'il était fermé
- [FIXED] Correction de `EntityRenderer`
- [ADDED] Ajout du joystick manette
- [ADDED] Ajout du multijoueur
- [ADDED] Ajout du mode coopératif local
- [CHANGED] Mise à jour du système coop
- [ADDED] Système de magie personnalisable
- [ADDED] Particules magiques lumineuses
- [ADDED] Possibilité de monter sur les animaux apprivoisés
- [CHANGED] Modification de la vitesse du joueur
- [ADDED] Interaction avec les animaux sous la souris
- [CHANGED] Correction de l'ordre de rendu des montures

### 27 juin

- [FIXED] Correction de la lecture de la musique

### 28 juin

- [CHANGED] Correction du système musical
- [ADDED] Ajout d'arbres procéduraux top-down
- [ADDED] Génération procédurale de chênes
- [ADDED] Ajout de ruches d'abeilles dans les arbres
- [CHANGED] Modification de la physique des voitures
- [ADDED] Gestion des bordures sur les farmland humides
- [CHANGED] Refonte des voitures en tant qu'entités
- [FIXED] Mise à jour de la hitbox des voitures
- [REMOVED] Retrait de l'entité voiture

### 29 juin

- [FIXED] Correction du mode coop
- [ADDED] Multijoueur local
- [ADDED] Inventaire multijoueur
- [FIXED] Correction de la génération des PNJ dans les maisons
- [PERF] Optimisations du projet
- [CHANGED] Connexion des tuiles intérieures
- [ADDED] Génération de villages structurés
- [CHANGED] Augmentation de la zone d'attaque des ennemis

### 30 juin

- [ADDED] Ajout de l'attaque en charge du cochon
- [CHANGED] Amélioration des câbles électriques
- [ADDED] Ajout de hintbox aux machines à vapeur
- [REMOVED] Suppression de la dynamo
- [ADDED] Ajout de la lampe à vapeur
- [FIXED] Correction du système de lampe à vapeur
- [FIXED] Correction de la sauvegarde des câbles électriques
- [FIXED] Empêchement du spawn des mobs dans l'eau

## Juillet 2026

### 1er juillet
- [ADDED] Ajout des ecureuils
- [FIXED] Correction de plusieurs erreurs dans `Program.cs`
- [FIXED] Correction de l'`AttackCooldown`

### 2 juillet
- [FIXED] Correction de l'erreur `CS0200`
- [CHANGED] Mise à jour du multijoueur en ligne

### 3 juillet
- [FIXED] Correction des erreurs de `Network.cs`
- [FIXED] Correction d'erreurs de compilation de `Program.cs`
- [CHANGED] Mise à jour du système de lumière

### 4 juillet
- [FIXED] Correction des erreurs de nullabilité dans `Program.cs`
- [ADDED] Ajout des normal maps
- [FIXED] Correction de la lumière traversant les murs avec les normal maps
- [ADDED] Système de lumière par tuile avec propagation
- [ADDED] Système d'attaque en trois phases pour les mobs
- [ADDED] Gestion de la transparence des éléments
- [ADDED] Ajout d'`InstrumentUI`
- [FIXED] Correction de `MusicPlayer`
- [FIXED] Correction de la musique muette
- [CHANGED] Amélioration du système d'équipement
- [FIXED] Correction des accessoires non affichés
- [FIXED] Correction de l'affichage des casques et accessoires
- [FIXED] Correction de l'équipement des écharpes
- [FIXED] Correction de températures trop élevées
- [FIXED] Correction des incohérences entre température et biome
- [ADDED] Gestion de la température des équipements
- [FIXED] Correction de la génération du monde
- [FIXED] Correction des crafts dupliqués

### 5 juillet
- [ADDED] Ajout du Khamsin, boss du desert, ainsi que sa flute, pour lui permettre de spawn
- [FIXED] Correction du fonctionnement des bateaux
- [CHANGED] Amélioration du système de plongée
- [CHANGED] Refonte du système de lumière inspirée de Core Keeper
- [FIXED] Correction de la disparition de l'équipement de drague
- [CHANGED] Mise à jour des données de catégorie des chapeaux

### 6 juillet
- [ADDED] Ajout de l'accès aux étages des maisons

### 8 juillet
- [ADDED] Ajout de la tenue antigaz, du masque a gaz, du chapeau pork pie, ainsi que les lunettes classes
- [ADDED] Ajout des bannieres, avec les patterns aleatoires
- [ADDED] Ajout d'un système d'équipement modulaire
- [ADDED] Ajout de la texture du chapeau de sorcière
- [FIXED] Correction de l'affichage des bannières sur les porte-bannières
- [FIXED] Correction de la perte de métadonnées des bannières
- [ADDED] Ajout de nouvelles textures d'équipement
- [FIXED] Correction de l'affichage des jambes avec les équipements
- [FIXED] Correction de l'affichage des jambes avec la tenue anti-gaz
- [FIXED] Correction des interactions bloquées par l'UI

### 9 juillet
- [CHANGED] Meilleur menu principal, avec interface en carousel
- [ADDED] Ajout de la gestion de temperatures
- [ADDED] Ajout de la barre de vie du boss en haut de l'ecran

### 10 juillet
- [CHANGED] Amelioration du systeme de toit : plus de toits plats
- [ADDED] Ajout des noms des PNJ
- [ADDED] Mise en place du jeu P2P en ligne
- [CHANGED] Amélioration des toits générés pour les maisons top-down

### 17 juillet
- [CHANGED] Réduction des hitbox des entités
- [FIXED] Correction de la quantité dans le menu créatif
- [FIXED] Correction des erreurs `CS0126`

### 23 juillet
- [FIXED] Fix du bug d'ecran noir des grottes

### 24 juillet
- [CHANGED] Réduction de la hauteur des barres de vie
- [ADDED] Affichage de l'item tenu dans la main gauche
- [FIXED] Correction du banc

### 25 juillet
- [ADDED] Ajout des yeux reactifs a la position de la souris
- [ADDED] Ajout du chapeau de MrBeast
- [ADDED] Ajout de l'UI endurance/eau/faim/vie/item en main (en haut a gauche)
- [ADDED] Ajout du systeme de faim, de soif et d'endurance
- [ADDED] Ajout des chats
- [ADDED] Ajout des bulles de dialogue en nine-slice

### 26 juillet
- [ADDED] Ajout des pigeons
- [ADDED] Ajout des gobelins
- [CHANGED] Amelioration du systeme d'attributs d'entites
- [ADDED] Ajout des cordons bleus
- [ADDED] Ajout du masque corbeau
- [ADDED] Ajout du fromage, de la puree
- [CHANGED] Amelioration des villages : bancs et puits apparaissent, les villageois peuvent s'asseoir sur les bancs
- [ADDED] Configuration de l'hôte multijoueur
- [CHANGED] Ajout de presets de variantes pour les pigeons

### 27 juillet
- [ADDED] Ajout des niveaux et des skills
- [ADDED] Les PNJ peuvent rejoindre notre guilde
- [CHANGED] Refonte du menu de craft
- [ADDED] Ajout de la pluie
- [CHANGED] Amelioration de l'IA des entites tamees
- [FIXED] Correction de la hotbar vide
- [FIXED] Correction de l'erreur `CS0103`
- [ADDED] Ajout du mini-jeu de pêche
- [ADDED] Ajout du cache du menu principal

### 28 juillet
- [ADDED] Ajout des differentes langues du jeu, via les parametres
- [ADDED] Ajout des controles customisables
- [ADDED] Ajout du menu de commerce avec les PNJ
- [CHANGED] Amelioration du menu d'items (godmode) : interface plus propre avec categories
- [CHANGED] Amélioration des bordures de biomes
- [CHANGED] Ajout de textures aux onglets
- [ADDED] Sauvegarde des items dans l'offhand
- [ADDED] Possibilité de monter sur les cochons apprivoisés
- [FIXED] Correction de la sauvegarde des paniers
- [CHANGED] Agrandissement du titre du menu pause
- [ADDED] Choix de l'intégration des langues
- [CHANGED] Agrandissement des options de langue avec drapeaux
- [ADDED] Configuration personnalisable des contrôles
- [CHANGED] Réduction de l'espace entre les onglets
- [CHANGED] Refonte du menu d'options avec textures et personnalisation des touches
- [FIXED] Correction d'erreurs de compilation dans `Program.cs`
- [CHANGED] Modification du menu des contrôles
- [CHANGED] Amélioration du menu de création du monde
- [CHANGED] Mise à jour du multijoueur en ligne
- [FIXED] Correction de la disparition des PNJ

### 29 juillet
- [FIXED] Mode en ligne fonctionnel et repare (LAN comme en ligne)
- [ADDED] Ajout de l'interface de boutons cliquables pour un hub d'interfaces
- [ADDED] Ajout du mode PVP
- [CHANGED] Amelioration du tchat en multijoueur
- [ADDED] Ajout de `TraderUI`
- [CHANGED] Amélioration de l'interface commerçant
- [CHANGED] Amélioration du rendu des arbres
- [ADDED] Particules segmentées pour le Worm
- [CHANGED] Amélioration des ombres du Worm
- [CHANGED] Modification de `TraderUI`
- [CHANGED] Ajout de la personnalisation de la couleur de peau dans le menu
- [CHANGED] Ajout d'une interpolation réseau pour fluidifier le multijoueur
- [CHANGED] Renforcement des permissions réseau
- [CHANGED] Renforcement de `Network.cs`
- [ADDED] Persistance des invités dans `Network.cs`
- [FIXED] Correction du rendu des items
- [CHANGED] Amélioration de l'interface multijoueur
- [FIXED] Correction des PNJ sans quêtes ni commerce
- [ADDED] Synchronisation de la pluie en multijoueur
- [ADDED] Ajout d'une icône pour expulser un joueur
- [ADDED] Ajout du chat vocal de proximité
- [FIXED] Correction des bugs des grottes
- [FIXED] Correction des entrées et sorties de grottes
- [FIXED] Correction du placement de tuiles par les clients

### 30 juillet
- [FIXED] Reparation des PNJ, pathfinding fonctionnel et plus optimise
- [ADDED] Ajout des differents boss : le Worm, le Gulper, Biggie, ainsi que la vague de monstres
- [ADDED] Ajout de la gourde, et du cueilleur d'eau de pluie
- [ADDED] Ajout des items de boss
- [ADDED] Ajout du chapeau chinois
- [ADDED] Ajout de la tortue et de sa carapace
- [ADDED] Ajout des maracas
- [FIXED] Correction des interactions pendant l'hébergement d'une partie
- [FIXED] Correction de la croissance des plantes sur sol sec
- [ADDED] Ajout de la barre d'interface rapide
- [ADDED] Ajout de la barre d'accès rapide
- [FIXED] Correction de la fermeture de la guilde avec Echap
- [ADDED] Ajout d'un menu d'items dans la barre UI
- [FIXED] Suppression du double dessin des particules
- [CHANGED] Amélioration de l'interface de teinture
- [ADDED] Ajout des gourdes
- [ADDED] Ajout d'une interface de visualisation de la musique
- [CHANGED] Amélioration de la synchronisation multijoueur

### 31 juillet
- [ADDED] Ajout du casque de Chevalier
- [FIXED] Puits et gourde desormais fonctionnels
- [ADDED] Ajout des runes craftables permettant d'ameliorer ses parties d'armures
- [ADDED] Ajout de Rich Presence
- [FIXED] Correction de la génération des villages

## Août 2026

### 1er août
- [FIXED] Correction des PNJ immobiles la nuit
- [CHANGED] Organisation de la sortie de compilation
- [ADDED] Ajout du menu Bestiaire

### 2 août
- [CHANGED] Amelioration du cercle satanique
- [ADDED] Ajout du demon
- [ADDED] Ajout des armures d'animaux
    - Lorsqu'un set d'armure complet est equipe, on a un buff correspondant
        - buff crabe : petit crabe de compagnie
- [ADDED] Ajout des potions, systeme de recette via le chaudron avec effets
- [ADDED] Ajout de la baguette
- [ADDED] Ajout du beret
- [CHANGED] Renforcement de la logique des caves

### 3 août
- [PERF] Optimisation du multithreading
- [CHANGED] Refonte du système d'items et de leurs métadonnées
- [ADDED] Ajout de pistolets, amelioration du mode de tir
- [CHANGED] Amelioration du fonctionnement des items, meilleures metadonnees
- [ADDED] Ajout du laiton, du bronze
- [ADDED] Ajout du repos dans les lits
- [CHANGED] Amelioration du fonctionnement de l'electricite
- [ADDED] Ajout des portes logiques electriques
- [ADDED] Ajout des batteries portables

### 4 août
- [FIXED] Reparation des bateaux
- [FIXED] Synchronisation du mode en ligne (fix)
- [ADDED] Ajout des arbres avec feuilles qui tombent, feuillage colore
- [ADDED] Ajout des pousses d'arbres
- [ADDED] Ajout du caca pour fertiliser le sol
- [ADDED] Les poules pondent aleatoirement des oeufs, et les cochons font caca
- [CHANGED] Amelioration du menu de mort : messages customs et animation de mort
- [CHANGED] Amelioration de l'entree dans les caves (petite animation)
- [FIXED] Reparation des quetes ne fonctionnant plus a une certaine distance : veritable cahier des quetes

### 5 août
- [CHANGED] Réduction du filtre vert
- [CHANGED] Orientation du jeu vers davantage de fonctionnalités d'aventure
- [CHANGED] Planification de la fragmentation du code en fichiers séparés
- [ADDED] Ajout des quêtes de dressage des animaux
- [ADDED] Ajout d'une physique de drift pour les véhicules
- [ADDED] Ajout des portails, avec teleportation vers tout portail connu
- [FIXED] Fix des villages : desormais bien plus grands, avec plus de villageois
- [ADDED] Ajout de la neige dans les biomes froids au lieu de la pluie
- [FIXED] Reparation du systeme de temperature
- [CHANGED] Amelioration du rendu : le personnage tremble de froid
- [CHANGED] Les sorties de grotte laissent passer la lumiere de l'exterieur
- [ADDED] Ajout de la durabilite des items et de la nourriture pourrie (selectionnable en creation de monde)

### 6 août
- [ADDED] Ajout des animaux dresses qui suivent non seulement le joueur, mais aussi les entites qui les possedent
- [CHANGED] Fragmentation des fichiers World.cs et Program.cs en morceaux plus lisibles
- [ADDED] Les mobs ont desormais des montures (ex: groupes de gobelins avec leurs loups)
- [CHANGED] La methode de drop de loot a ete amelioree : passage d'une liste definie a l'utilisation de pourcentages de chances et de metadonnees
- [CHANGED] Les entites spawnes sont desormais reellement intelligentes
- [FIXED] Reparation des informations sur les potions
- [ADDED] Ajout des slimes avec IA intelligente suivant sa cible en sautant

### 7 août
- [FIXED] Correction de l'IA des slimes
- [ADDED] Ajout du saut des slimes
- [CHANGED] Mise à jour du système de lumière
- [CHANGED] Amelioration des grottes : ajout des biomes souterrains, notamment le desert souterrain
- [FIXED] Plus de pluie dans les caves (fix)
- [ADDED] Ajout des petits oasis dans les deserts
- [ADDED] Differentes teintes de slimes selon les biomes
- [ADDED] Ajout des insectes : coccinelles et scarabees
- [FIXED] Les aliments de differents niveaux de pourriture peuvent se stacker (fix)
- [ADDED] Nouvelles pierres precieuses dans les grottes du desert : grenat, ambre, topaze, onyx, opale et obsidienne
- [ADDED] Ajout du boss Genie, apparaissant avec la lampe magique
- [ADDED] Ajout des coffres souterrains
- [CHANGED] GROSSE MISE A JOUR DES LUMIERES : ne traverse plus les murs !
- [CHANGED] En godmode, le joueur ne perd plus d'endurance et sa vitesse de course est doublee
- [PERF] Optimisation des tuiles vers metadonnees pour une meilleure synchronisation
- [ADDED] Ajout de l'effet de frappe lorsqu'une entite est blessee
- [PERF] Optimisation de items.json, recipes.json, worldobjects.json : descriptions et noms sont desormais dans les fichiers de langue
- [PERF] Optimisation importante des entites hors ecran : augmentation large des FPS dans les villages
- [ADDED] Ajout de nouvelles potions : force, resistance, lumiere, resistance au chaud/froid, invisibilite
- [ADDED] Ajout de la satiete et nouveaux effets
- [CHANGED] Mise a jour du mode en ligne : meilleure synchronisation
- [ADDED] Ajout du menu de creation du personnage pour les clients

### 9 août
- [CHANGED] Amelioration du menu principal : menu calme avec le jeu en fond
- [CHANGED] Les mobs ne nous suivent plus a l'infini : ils abandonnent si on s'eloigne trop ou qu'ils sont trop loin de leur point d'origine
- [CHANGED] Amelioration du rendu de l'eau (bof)
- [ADDED] Ajout du golem d'argile
- [PERF] Optimisation du systeme de minerais dans les grottes avec les metadonnees de tuiles
- [FIXED] Les insectes (papillons, libellules, feufollets) n'apparaissent plus partout, notamment dans les caves

### 10 août
- [CHANGED] Mise à jour du système de changelog et de documentation Discord
- [CHANGED] Personnalisation du Markdown utilisé pour la documentation du projet
- [CHANGED] Nouvelle texture des moutons, avec variations
- [ADDED] Ajout des hyenes en troupes dans le desert, attaquant le joueur
- [CHANGED] Le fond du menu principal est flou lorsqu'on choisit son personnage (a revoir)
- [ADDED] Dans le desert et les savannes, les gobelins ont des hyenes de compagnie
- [CHANGED] Les PNJ vendent leurs items : ils ne sont plus gratuits
- [ADDED] Les animaux spawnent en troupes
- [CHANGED] Le menu de commerce a ete retouche : on voit le prix des items
- [FIXED] Les quetes ont ete fixees (recherche d'animal de compagnie)
- [ADDED] Les PNJ donnent de l'argent comme recompense de quete
- [CHANGED] Amelioration du boss Genie (bruits et rythme)
- [CHANGED] La tenue portable peut desormais etre selectionnee entre manches courtes/longues, ouverte/fermee
- [ADDED] Ajout du fez
- [CHANGED] Amelioration du menu principal : lors de la selection du monde, les personnages se promenent en fond et discutent entre eux. On peut switcher de personnage avec les fleches.
- [PERF] Le menu principal est decharge lorsqu'on charge une partie
- [CHANGED] Le jeu n'est plus bride a 60 FPS
- [CHANGED] Amelioration du pathfinding de retour
- [PERF] Optimisation des PNJ et du lag (cache + reparation systeme d'equipement Feet)
- [FIXED] Retrait du trou bugge dans les murs de maisons
- [ADDED] Ajout de la casserole
- [CHANGED] Amelioration du menu de miroir

### 11 août
- [CHANGED] Mise à jour du système Markdown
- [ADDED] Les trades avec les PNJ permettent de faire monter le niveau d'amitié
- [ADDED] Le menu de guilde permet de modifier le stuff des équipiers
- [ADDED] Le menu de guilde s'ouvre avec G (par défaut)
- [ADDED] Bulles d'infos sur les températures
- [ADDED] Ajout des babouches, permettent de marcher sur le sable chaud sans se brûler
- [CHANGED] Mise à jour du menu de création du monde
- [FIXED] Réparation de la sauvegarde du blason de guilde
- [ADDED] On peut choisir ma seed du monde à sa création
- [CHANGED] Amélioration du menu d'équipement, on peut directement glisser les équipements de l'inventaire au personnage
- [FIXED] Réparation des items non transférés de ID nombre à ID nom (balles, poo)
- [ADDED] Ajout des scorpions
- [ADDED] Ajout de l'empoisonnement
- [CHANGED] Les sauvegardes (représentés par des joueurs) disparaissent dans des nuages de particules
- [CHANGED] Amélioration de la map
- [CHANGED] Amélioration de la génération aléatoire de la map
- [CHANGED] Les instruments jouent désormais en .mid et non plus des .mp3 pré-enregistrés

### 12 août
- [ADDED] Ajout du masque de la peste
- [CHANGED] Amélioration du système d'affichage du nom d'item en cas de noms manquant
- [CHANGED] Réduction des quantités de spawn de troupes
- [ADDED] Ajout du bolet, avec son système de lâcher de poison à sa proximité.
- [FIXED] Réparation du sac à main non lu par l'ordinateur
- [CHANGED] Mise à jour des habits teignables
- [ADDED] Ajout de la kippah
- [FIXED] Réparation de la neige et des patterns dessus
- [FIXED] Réparation du rendu de la barre d'icônes d'infos
- [ADDED] Les arbres et fleurs bougent désormais avec le mouvement du vent, de façon naturelle

### 13 août
- [PERF] Réduction du code et suppression de messages inutiles
- [FIXED] Correction du problème de console
- [REMOVED] Retrait des messages de setup lorsqu'on démarre le jeu
- [CHANGED] Amélioration des plages : ajout de la bordure et des textures de variation
- [CHANGED] Amélioration de l'UI du menu principal
- [ADDED] Ajout de la coupe de Musclor
- [CHANGED] Mise à jour du petit menu du démarrage du jeu : le logo est désormais sur un rond qui correspond à la bulle de chargement

### 14 août
- [CHANGED] Amélioration du rendu de l'eau : caustiques, teintes océaniques et effets de vagues

### 20 août
- [PERF] Optimisation générale des performances du jeu
- [CHANGED] Je suis désormais le seul créateur du jeu

### 21 août
- [CHANGED] Les PNJ ont une IA bien plus développée : ils vont chercher des éléments, puis les placer dans leurs coffres

### 24 août
- [ADDED] Ajout des contrôles manette dans les paramètres de touches
- [CHANGED] Amélioration du rendu du menu des contrôles, et on doit sauvegarder les changements pour valider
- [FIXED] Fix des incohérences entre le menu principal et la partie chargée
- [FIXED] Le rendu des items au sol qui bougent lorsqu'on passe dessus avec l'inventaire plein est fix

### 25 août
- [CHANGED] Le lore du jeu est profondément remanié autour de la lumière, des âmes, des corps et de l'obscurité
- [CHANGED] Le concept du monde est défini autour d'âmes lumineuses provenant des étoiles et cherchant des corps à habiter
- [CHANGED] Introduction du concept d'« ascension » et de réincarnation
- [ADDED] Ajout de la notion de mauvaises âmes issues d'âmes ayant trop longtemps erré sans corps
- [ADDED] Ajout de plusieurs philosophies et croyances opposées autour de la vie, de l'ascension et de l'obscurité
- [ADDED] Ajout du concept de fusion de plusieurs âmes dans un même corps
- [ADDED] Ajout du concept d'individus capables de contenir plusieurs âmes
- [CHANGED] Le joueur peut potentiellement devenir un être capable de contenir plusieurs âmes au cours de son évolution
- [CHANGED] Le projet est progressivement associé au nouveau nom --Soulfract--
- [CHANGED] Grosse amélioration du lore du jeu, qui sera désormais appelé Soulfract

## Septembre 2026

### 1er septembre
- [CHANGED] Mise à jour du système d'items, désormais les items craftables affichés dépendent des ingrédients déjà possédés auparavant

### 2 septembre
- [CHANGED] Définition d'une logique de mort dans laquelle le corps du joueur meurt réellement
- [CHANGED] Définition d'une logique de réapparition permettant au joueur de revenir dans un corps identique malgré la mort du corps précédent

### 13 septembre
- [FIXED] Réparation des items donnés à la création du monde qui ne se stackaient pas
- [CHANGED] Amélioration du rendu des arbres : impact lorsque frappé
- [FIXED] Réparation des PNJ bloqués dans les murs
- [FIXED] Fix des quêtes redirigeant vers un PNJ qui n'existe pas
- [FIXED] Fix des PNJ qui marchent dans le vide à l'infini
- [FIXED] Fix des barres de vies des entités
- [FIXED] Fix des bras des joueurs synchronisés
- [FIXED] Fix du rendu du morph lorsqu'une espèce avec des spécificités est morph
- [PERF] Petites optimisations à travers tout le code pour les performances
- [FIXED] Le personnage arrête de marcher si on appuie dans deux directions opposées
- [FIXED] Le rendu des toits/ des arbres n'est plus buggé en bas de l'écran

### 14 septembre
- [CHANGED] Réflexion sur une solution d'hébergement du jeu disponible 24 h/24 et 7 j/7
- [FIXED] Fix des potions buggées et incohérentes (bouteille vide non utilisable)
- [ADDED] Ajout du menu de débug pour les informations sur les items tenus
- [CHANGED] Amélioration des styles de tenue possible (plus médiéval)
- [CHANGED] Amélioration des métadonnées et du système de sauvegarde qui y est lié
- [CHANGED] Amélioration du rendu des interactions au dessus des tuiles
- [ADDED] Ajout du mode de jeux de cartes avec les PNJ : le Pouilleux

### 15 septembre
- [CHANGED] Amélioration du Pouilleux
- [CHANGED] Renforcement de l'IA des PNJ
- [ADDED] Le Pouilleux est jouable en multijoueur
- [PERF] Optimisation des performances/ du mode en ligne

### 16 septembre
- [FIXED] Réparation des villages : il n'y a plus la maison invisible
- [ADDED] Ajout des hitbox smooth entre entités : on peut désormais pousser les entités, et inversement
- [CHANGED] Amélioration de l'IA des PNJ : plus stable, moins laggy
- [CHANGED] Amélioration du menu F3 : on voit désormais les intentions des entités au dessus de leurs têtes
- [CHANGED] Amélioration du tchat et autres boîtes de texte : plus flexibles, les raccourcis copier coller couper sont possibles etc..
- [FIXED] Réparation de l'équipement des items comme le coat avec des métadonnées et un rendu custom
- [CHANGED] Déplacement du tchat pour éviter la superposition avec d'autres potentielles interfaces
- [CHANGED] Amélioration du rendu du tchat, plus efficace et joli
- [CHANGED] Agrandissement des informations sur le menu de craft
- [CHANGED] Amélioration du rendu du menu de craft, meilleure interface et plus pratique
- [CHANGED] Amélioration des cases de l'inventaire, avec effet de bordure et interaction
- [ADDED] Ajout du masque du pigeon
- [ADDED] Ajout des tournesols

### 17 septembre
- [CHANGED] Les plantes sont désormais mettables directement dans les pots !
- [CHANGED] Les PNJ ont désormais des teintes plus classiques et naturelles, ainsi qu'un style vestimentaire plus contrôlé
- [CHANGED] Les aliments qui pourrissent se stackent désormais naturellement, avec une moyenne pondérée de la pourriture lors du mélange des piles
- [CHANGED] Amélioration des villages : ajout des champs, et des PNJ qui s'en occupent
- [ADDED] Ajout des tentes
- [ADDED] Dans les grottes, on peut désormais trouver des équipes d'explorateurs avec leurs camps
- [ADDED] Hitbox du joueur désormais visibles
- [ADDED] PNJ intelligents : ils peuvent désormais aller dormir dans des lits, et peuvent forcer une place s'il n'y en a vraiment plus.
- [ADDED] Ajout d'un tas de champignons
- [FIXED] Fix des différents items de planchers qui ne marchaient plus : à la place, un seul item avec un style qu'on définit lors du craft.
- [ADDED] Ajout des oiseaux volants : ils peuvent sortir des arbres, et se déplacer librement, marcher ou voler.
- [CHANGED] Mise à jour des textures de pantalon, de chaussures
- [ADDED] Ajout des commandes /time set
- [ADDED] Ajout de la téléportation via la map (godmode)
- [FIXED] Réparation des tremblements de l'écran à une grande distance du centre
- [FIXED] Réparation des accessoires qui ne s'affichaient pas bien.
- [CHANGED] Mise à jour du "Activer la console de debug en arrière-plan ? [O/N] " : à la place, le jeu détecte simplement s'il y a une console ou non
- [CHANGED] Amélioration des métadonnées des conteneurs de liquide. Plus d'information éparpillée dans le code, à la place, une clé supplémentaire et facultative dans le items.json.
- [ADDED] Ajout de bruits d'ambiance, plus agréable

### 18 septembre
- [ADDED] Ajout d'un nouvel arbre : le sapin (spruce), qui lâchera derrière lui des pommes de pins et non des glands.
- [FIXED] Fix de l'overlapping des gros sprites de tuiles entre eux
- [ADDED] Ajout de la couronne
- [ADDED] Les PNJ peuvent s'asseoir chez eux, puis dormir le soir
- [FIXED] Le mur sans hitbox de la maison est fix
- [ADDED] Les feux de camps font de la fumée
- [FIXED] Les lumières fonctionnent désormais bien hors écran
- [FIXED] Fix du socle oeil et de ses attentes
- [ADDED] Ajout du switch rapide d'items en cliquant sur une case et la touche de la hotbar avec le curseur dessus
- [CHANGED] Renforcement de l'IA des PNJ : plus d'hésitations, ils peuvent aller directement sur les bancs, ranger les objets le soir etc..
- [ADDED] Ajout de nouvelles maisons au village
- [CHANGED] Amélioration du rendu de l'herbe des forêts
- [FIXED] Fix du rendu des toits prioritaire malgré le y index
- [CHANGED] Amélioration des routes de village
- [CHANGED] Mise à jour du rendu intérieur des facades de murs
- [FIXED] Fix des sacs à dos sans contenu après recharge
- [CHANGED] Mise à jour de la tooltip box, meilleur rendu et plus d'informations
- [CHANGED] Amélioration du rendu du texte, plus de léger flou
- [CHANGED] Mise à jour du Pouilleux
- [ADDED] Ajout du Solitaire
- [CHANGED] Amélioration des menus de cartes, possibilité de jouer directement via le deck de cartes
- [ADDED] Ajout du jeu d'échec, avec animations etc..
- [FIXED] Fix du rendu des cheveux devant le cou + ajout de nouvelles coiffures
- [CHANGED] Amélioration de la machine à vapeur, meilleur fonctionnement pour moins de matériaux.
- [CHANGED] Amélioration de la tooltip box de la machine à vapeur.
- [ADDED] Ajout de l'horloge de grand-père, avec aiguilles affichant le temps en jeu.
- [CHANGED] Réduction de la quantité récupérée par un collecteur d'eau de pluie.
- [ADDED] Ajout du Locust - pas d'utilité pour l'instant, à voir à l'avenir
- [CHANGED] Mise à jour de la position de la liste des interactions possibles au dessus d'une tuile : position intelligente qui dépend de la taille de la tuile.
- [CHANGED] Les couleurs ne sont pas des informations à part, ce sont des métadonnées sur les items.

### 19 septembre
- [CHANGED] Les villageois labourent leurs champs eux mêmes
- [ADDED] Ajout des chapeaux en paille
- [CHANGED] Renforcement de l'IA des PNJ, ils ne marchent plus à l'infini au pas de la porte
- [ADDED] Les PNJ posent leurs affaires dans le coffre le soir
- [FIXED] Fix de toutes les textures manquantes (levier, plaque de pression, coat, golem, etc..)
- [ADDED] Ajout de l'item moisi
- [CHANGED] Mise à jour du système d'engrais : indication directement dans les données de l'item
- [CHANGED] Amélioration du rendu de l'attaque des entités
- [ADDED] Les entités aussi font du bruit en marchant
- [ADDED] Les entités pouvant être portées par le joueur ont désorais une information dans leur donnée d'espèce
- [FIXED] Fix des poules qui ne pondaient plus des oeufs
- [CHANGED] REVISITE ENTIERE DU MENU PRINCIPAL

### 20 septembre
- [CHANGED] Amélioration du menu de chargement, en lien avec le menu principal
- [FIXED] Appuyer sur une touche de mouvement nous fera sortir d'un endroit assis, pas n'importe quelle autre touche
- [ADDED] Le joueur peut choisir sa place sur le banc
- [CHANGED] Mise à jour du titre Heroes -> Soulfract
- [PERF] Optimisation du système de sauvegarde : parties bien plus légères
- [ADDED] Ajout du beurre
- [ADDED] Ajout des lunettes
- [ADDED] Ajout d'un système de cuisson pour les aliments
- [PERF] Amélioration du lag lors du chargement des chunks
- [CHANGED] Mise à jour du menu de debug pour indiquer les raisons du lag
- [CHANGED] Le jeu a désormais un rendu basé sur la taille de l'écran
- [CHANGED] Rendu de la zone de placement d'un gland mis à jour pour correspondre à la taille de l'arbre, pas de la pousse
- [FIXED] Réparation de la logique d'affichage des équipements et items colorables
- [FIXED] Mise à jour du mannequin qui ne fonctionnait plus
- [ADDED] Les PNJ peuvent équiper le stuff trouvé
- [ADDED] Ajout du chapeau haut de forme
- [ADDED] Ajout des lunettes steampunk
- [CHANGED] Mise à jour des récoltes
- [ADDED] Ajout du coton
- [CHANGED] Amélioration de certains plants : le coton par exemple ne va pas être complètement déraciné lorsqu'il a fini de pousser; on récupère juste ce qui est dessus.
- [ADDED] Ajout du craft du tissu
- [ADDED] Ajout de textures de liquide pour le seau
- [CHANGED] Meilleure flexibilité pour les conteneurs de recettes de potions : tout le système mis à jour
- [ADDED] Ajout des boutons d'importation et d'exportation de partie dans le menu de choix de la sauvegarde
- [ADDED] Ajout du bouton des changelogs, avec petite interface permettant de voir les mises à jour selon la date
- [ADDED] Ajout du jet d'item, comme les oeufs ou les boules de neige
- [CHANGED] Les PNJ vendent des items en lien avec leurs professions
- [ADDED] Ajout de la mallette
- [FIXED] Mise à jour de la logique de stockage des items conteneurs (ex : panier / sac à dos)
- [FIXED] Les paupières sont de la couleur de la peau lorsque le personnage cligne
- [ADDED] Les arbres avec une ruche d'abeille donnent bel et bien de la cire d'abeille
- [ADDED] Ajout du craft de la bougie
- [ADDED] Les pingouins, crabes et tortues apparaissent désormais naturellement
- [CHANGED] Modification des murs de pierre apparaissant dans les biomes enneigés -> Statues Moai à la place
- [FIXED] Réparation du rendu du bateau
- [ADDED] Ajout du cache-oeil
- [CHANGED] Modification de la génération de la map, retrait temporaire des hauteurs
- [CHANGED] Amélioration du rendu de la hotbar classique ainsi que de ses fonctionnalités

### 21 septembre
- [CHANGED] Mise à jour du changelog du projet sous le nom **Soulfract**

### 22 septembre
- [CHANGED] Mise à jour du menu pour rejoindre la partie

### 27 septembre
- [PERF] Les textures complètement à l'ombre ne s'affichent plus
- [ADDED] Ajout d'un cooldown entre les attaques du joueur
- [FIXED] Les PNJ dans les grottes se déplacent désormais légèrement, et vont même dormir dans les tentes
- [CHANGED] Les propagation de lumière sont en forme de cercles plus naturels et non plus de losanges
- [CHANGED] Amélioration du rendu général de la lumière; les murs bloquent vraiment la lumière, elle rend mieux lorsqu'il y a plusieurs sources
- [CHANGED] Animation de trigger des entités bien plus courte
- [FIXED] Rendu des pixels transparents lors de l'intro réglé
- [ADDED] Les PNJ ont désormais des guardes dans leur village, attaquant les ennemis à proximité
- [CHANGED] Meilleure IA d'entités targetted
- [CHANGED] Les entités n'ont plus de fearRange,attackRange et agressionRange mais simplement une visionRange qui définira leurs interactions selon leur hostilité
- [PERF] Meilleure logique de sauvegarde, fichiers plus légers
- [FIXED] Les effets de température n'apparaissent plus dans le menu principal si on quitte subitement
- [FIXED] Les spécificités des entités se re-affichent lorsqu'on a l'écran entièrement dézoomé
- [ADDED] Une entité blessée gouttera de sang
- [FIXED] Meilleur système d'écho des entités targetted
- [FIXED] Les joueurs ne sont plus freeze lorsqu'ils écrivent dans le tchat
- [CHANGED] Lorsqu'on ferme une interface, le personnage ne frappe plus car la logique de fermeture d'UI se fait après la vérification
- [ADDED] Les PNJ vendent des item en lien avec leur métier
- [CHANGED] Les boites de PNJ contiennent le nom du métier
- [CHANGED] Les menu de vente des pnj utilisent la même tooltip box que l'inventaire
- [ADDED] Les PNJ ont tout un système de conversation avec des sujets différents, et qui varient selon les habitants du village, leur situation etc..
- [ADDED] Les maisons de village sont désormais procédurales, avec différentes formes et pièces
- [CHANGED] Les PNJ ne drop plus les items au sol mais bien dans l'inventaire directement, avec une limite de quantité qui dépend de la limite max de stack de l'item
- [ADDED] Le jeu est compatible Steam Link (et donc mobile)
- [CHANGED] Une entité blessée va se soigner très doucement
- [ADDED] Ajout du slot des gants
- [CHANGED] Mise à jour du rendu des jambes, les deux tendent visuellement vers la droite

### 28 septembre
- [ADDED] Ajout du système d'alimentation du totem tiki pour faire apparaître un loup fantomatique avec des offrandes
- [ADDED] Ajout du raton laveur
- [FIXED] Les entités tamées respectent bien les règles du joueur
- [FIXED] La baguette magique re-fonctionne
- [ADDED] Système de shader sur l'animal fantomatique
- [ADDED] Un totem apparaît dans les villages occasionnellement, avec un animal protecteur
- [FIXED] Les PNJ ne demandent que des animaux possibles à tamer en animaux de quête
- [ADDED] Ajout de différentes teintes pour les loups
- [FIXED] Les entités regardent vers le curseur intelligement selon leur hauteur de tête et non plus selon celle d'un humain
- [FIXED] Affichage des ombres décalées dans certains menus
- [FIXED] Affichage des spécificités dans le menu de sélection
- [ADDED] Ajout du wagonnet, des rails et de leur utilisation

### 29 septembre
- [FIXED] Correction du son du vent, désormais diffusé en continu en extérieur
- [FIXED] Les personnages proposés à la connexion sont désormais propres au monde rejoint
- [FIXED] La synchronisation initiale du monde est envoyée par chunks pour éviter les déconnexions
- [FIXED] Le client traite progressivement les messages réseau pendant la synchronisation initiale
- [FIXED] Initialisation de l'entité joueur à la connexion réseau
- [FIXED] La sortie des donjons traverse désormais les murs épais; les salles sont plus grandes et moins nombreuses
- [CHANGED] Les torches murales des donjons sont stockées en métadonnée, dessinées au-dessus du mur et éclairent la salle
- [CHANGED] Les zones de dégâts agrandies et adaptées au gabarit des entités/joueurs sont visibles dans F3
- [FIXED] Les coups au corps-à-corps touchent correctement une cible qui chevauche la zone d'attaque
- [CHANGED] Les fiches F3 de l'item en main et de la tuile du joueur sont repliables

### 30 septembre
- [PERF] Optimisation de la sauvegarde des chunks des donjons
- [FIXED] Restauration des entités et du boss lors du rechargement d'une partie dans un donjon
- [FIXED] Correction de l'affichage des bannières dans les donjons
- [CHANGED] Mise à jour de la carte souterraine : affichage des murs et de toute la zone consultée
- [CHANGED] Réorganisation du menu F3 : panneaux verticaux, largeur uniforme et panneau principal repliable
- [ADDED] Ajout d'un menu de customisation des animations
- [FIXED] Sauvegarde des liaisons entre wagonnets et restauration du contenu des sacs à dos
- [CHANGED] Augmentation des dégâts du pistolet standard
- [CHANGED] ItemMenuUI : choix des métadonnées des runes et styles du manteau, quantité saisissable au clavier et boutons réalignés
- [ADDED] Recettes du beurre, du croissant, de la baguette, de la canne à pêche et du chapeau de paille
- [CHANGED] Apprivoisement des slimes en leur donnant du lait
- [ADDED] Stockage à une case pour les slimes : incubation et duplication des gemmes après 3 minutes, avec affichage dans leur corps et synchronisation en coop
- [FIXED] La fenêtre de stockage du slime ne bloque plus l'inventaire ni les autres interfaces
- [ADDED] Chat vocal multijoueur avec push-to-talk, micro désactivé ou activation permanente
- [CHANGED] Passage du chat vocal en mono 24 kHz et correction de l'ouverture répétée des règles UDP du pare-feu
- [ADDED] Tempêtes prolongées avec pluie renforcée, rafales, éclairs atténués et tonnerre
- [CHANGED] Transitions météo adoucies; état, minuteur et audio de la météo sont sauvegardés, réinitialisés par monde et mis en pause avec la partie
- [FIXED] Augmentation de la visibilité de la neige et suppression de la pluie sur les sols enneigés
- [FIXED] Les masques restent visibles au dézoom et passent devant les couvre-chefs et combinaisons
- [FIXED] Synchronisation des styles et couleurs d'yeux des joueurs distants
- [FIXED] Les projectiles touchent les entités synchronisées et les joueurs PVP distants
- [CHANGED] Les PNJ ciblent les joueurs distants et réagissent au joueur qui les attaque
- [FIXED] Récompenses de quête et progression d'amitié des marchands synchronisées pour les clients
- [ADDED] Customisation du menu d'équipement possible via les paramètres

---

# Potentiels ajouts futurs

## Items

1. [ADDED] Armures de gladiateur
2. [ADDED] Pogo stick
3. [ADDED] Armure tortue
4. [ADDED] Armure carton
5. [ADDED] Armure cactus
6. [ADDED] Sarbacane
7. [ADDED] Baton solaire : degats magiques (tres fort le jour en ete, useless la nuit en hiver), plus il fait chaud, plus c'est puissant (craft avec Pierre solaire etc.)
8. [ADDED] Cristaux de chaleur : brillent dans le noir, source d'énergie, peuvent servir a réchauffer en hiver

---

## Features

2. [ADDED] Items cadeaux par rapport a leurs métiers
4. [ADDED] Ajouter la musique
5. [ADDED] Les gobelins doivent drop quelque chose d'interessant
6. [ADDED] Emotes visuelles du personnage
7. [ADDED] Ajouter le systeme de musique de groupe
10. [ADDED] Ajouter des aliments fabricables (tartes, gateaux, plats)
13. [ADDED] La temperature la nuit pourrait etre plus froide
14. [ADDED] Ajout des effondrements dans les grottes (bruit vers chute de plafond)
15. [ADDED] Ajout des poches de chaleur : zones qui brulent
16. [ADDED] Vigne de sable : ralentit le joueur
17. [ADDED] Golem du desert
18. [ADDED] Les scarabées peuvent porter un item avec leurs pattes en volant
19. [ADDED] Structures de ruines : pieges avec loots, systeme de puzzle
21. [ADDED] Serpents des dunes : attaque rapide, sort du sable (debuff poison)
22. [ADDED] Cristaux solaires : se chargent sur des autels solaires, servent pour le baton solaire
23. [ADDED] Araignee desertique : agresse si on tombe dans son nid (debuff assoiffe)
24. [ADDED] Araignee de cristal : reflechit les projectiles (CAC uniquement)
25. [ADDED] Parasite de chaleur : s'accroche au joueur (drain de sante)
26. [ADDED] Armure de cristal solaire : boost jour (vitesse/force), nerf nuit (vitesse uniquement)
28. [ADDED] Ajouter le jetpack

---

## Fixs

2. [FIXED] Les generations de terrain ne sont pas synchronisees
5. [FIXED] Priorite du clic sur le bateau plutot que sur l'eau et boire