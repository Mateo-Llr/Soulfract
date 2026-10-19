# Découpage de Entity.cs

Le code est réparti en fichiers `partial` de la même classe `Soulfract.Entity`.
Les membres restent donc dans la même classe et peuvent continuer à accéder aux champs privés.

## Fichiers
- `Entity.Types.cs` : enums et `ProfessionData` qui étaient déclarés avant `Entity`.
- `Entity.CoreAndBossFields.cs` : Core fields, combat profile, and boss state data.
- `Entity.QuestsAndVillagers.cs` : Quests, villagers, social data, inventory and related properties.
- `Entity.AiPrioritiesAndMovement.cs` : Leash, AI priorities, movement momentum and stuck detection.
- `Entity.ConstructorAndPublicMethods.cs` : Constructor, equipment slots and public methods.
- `Entity.DecisionHelpersAndStoryCamp.cs` : Decision helpers and story camp behavior.
- `Entity.Update.cs` : Main update loop and related update logic.
- `Entity.AiMovementAndCombat.cs` : AI movement and unified combat entry points.
- `Entity.PrivateMethodsAndGenieBoss.cs` : Private helpers and Genie boss state machine.
- `Entity.WormBoss.cs` : Worm boss state machine.
- `Entity.TamedAnimalsAndArmorCompanion.cs` : Tamed animal commands and armor companion behavior.

## Intégration
1. Fais une copie de sauvegarde du projet.
2. Ajoute tous les fichiers `.cs` au même projet/dossier que l'ancien `Entity.cs`.
3. Supprime ou exclue l'ancien `Entity.cs` du projet pour éviter les définitions en double.
4. Vérifie que le projet utilise bien les mêmes options de compilation et lance un build.

Le découpage conserve le code source existant et change surtout son organisation. Je n'ai pas pu compiler le projet complet ici, car ses dépendances et fichiers de projet ne sont pas fournis.
