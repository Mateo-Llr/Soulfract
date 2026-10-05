# Distribuer Soulfract avec le bootstrapper

Distribuer uniquement `SoulfractBootstrapper.exe` depuis la dernière release publique de
[Mateo-Llr/Soulfract](https://github.com/Mateo-Llr/Soulfract/releases). Ce petit exécutable
natif vérifie les releases GitHub, télécharge et remplace le lanceur principal
`SoulfractLauncher.exe`, puis le démarre. Les utilisateurs n'ont besoin d'installer ni .NET
ni aucun autre runtime.

Le lanceur principal gère toujours les mises à jour du jeu. Il télécharge le jeu dans
`%LOCALAPPDATA%\Soulfract` et conserve les sauvegardes et préférences locales. Si le
bootstrapper se trouve à côté d'un `Soulfract.exe` existant, il installe le lanceur
principal dans ce même dossier ; les joueurs existants peuvent ainsi mettre à jour leur
installation sans déplacer les sauvegardes.

Le lanceur s'ouvre sur une interface graphique : il affiche la version disponible, les
dernières entrées de `Data/changelog.md`, l'état de la mise à jour et un bouton **Jouer**.
Il installe ou met à jour le jeu au démarrage, mais ne le lance qu'après un clic sur ce
bouton. Si aucune préférence de langue n'est encore enregistrée, le jeu démarre en
français.

## Publier une version

Après avoir poussé les modifications sur GitHub, créer et pousser un tag de version :

```powershell
git tag v1.0.3
git push origin v1.0.3
```

Le workflow GitHub Actions compile le jeu, le lanceur principal et le bootstrapper, puis
publie l'archive complète, le delta, et les deux exécutables sur la page Releases. Le
bootstrapper stable est le seul fichier à distribuer aux joueurs : chaque lancement lui
permet de récupérer la dernière version du lanceur principal, qui récupère à son tour la
dernière version du jeu. Le bootstrapper compare l'empreinte SHA-256 de l'exécutable
principal à celle publiée par GitHub : une nouvelle version du jeu ne lui fait donc
retélécharger le lanceur que si le fichier du lanceur a réellement changé.

Les installations déjà à la version immédiatement précédente reçoivent une archive
différentielle contenant uniquement les fichiers du jeu ajoutés ou modifiés (et la liste
des anciens fichiers à supprimer). Une première installation, ou une mise à jour après
avoir sauté une release, télécharge l'archive complète. Le jeu est publié sous forme de
fichiers .NET séparés afin qu'une modification du code n'oblige pas à retransférer tout
le runtime intégré dans un unique exécutable. Le passage de l'ancienne distribution
monofichier aux fichiers séparés peut toutefois nécessiter une mise à jour différentielle
plus volumineuse une seule fois ; les mises à jour suivantes ne retransfèrent que les
fichiers réellement modifiés.

La première version doit également être créée avec un tag `v*` afin que l'API GitHub
renvoie une release « latest ». Si GitHub est temporairement inaccessible, le bootstrapper
démarre la version du lanceur principal déjà installée ; le lanceur conserve son propre
flux de démarrage et de mise à jour du jeu.
