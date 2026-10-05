# Distribuer Soulfract avec le lanceur

Le lanceur Windows télécharge la dernière release publique de
[Mateo-Llr/Soulfract](https://github.com/Mateo-Llr/Soulfract/releases), installe le jeu
dans `%LOCALAPPDATA%\Soulfract` et le démarre. Il est autonome : les joueurs n'ont pas
besoin d'installer .NET. Si le lanceur se trouve dans un dossier contenant déjà
`Soulfract.exe`, il met à jour le jeu dans ce dossier. Les sauvegardes et préférences
présentes sont conservées lors des mises à jour. Pour un joueur qui possède déjà une
installation, placer le lanceur à côté de son `Soulfract.exe` avant de le démarrer
permet de mettre à jour cette installation sans déplacer ses sauvegardes.

## Publier une version

Après avoir poussé les modifications sur GitHub, créer et pousser un tag de version :

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Le workflow GitHub Actions compile le jeu et le lanceur, puis publie automatiquement
les deux fichiers sur la page Releases. Pour les joueurs, distribuer
`SoulfractLauncher.exe` ; le lanceur récupère ensuite l'archive du jeu.

La première version doit également être créée avec un tag `v*` afin que l'API GitHub
renvoie une release « latest ». La version du jeu reste installée si GitHub est
temporairement inaccessible ; le lanceur affiche alors une erreur et permet de
démarrer la version déjà présente.
