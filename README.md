# Lolo Minecraft Launcher

Launcher Windows vanilla pour Minecraft 26.2, en mode hors-ligne.

## Fonctionnalités

- Interface WinUI 3 sombre et légère.
- Choix du pseudo hors-ligne.
- Choix de la RAM : 2, 4, 6, 8 ou 12 Go.
- Catalogue des versions Mojang (releases et snapshots), installées à la demande.
- Profils Fabric téléchargeables à la demande, sans mod préinstallé.
- Minecraft 26.2 vanilla téléchargé à la demande, avec progression.
- Sélection d'un skin PNG local, conservé par profil.
- Aucun mod ni configuration existante n'est importé.
- Données générées dans `%APPDATA%\\.lolo-mc`.
- Bundle auto-extractible dans `LoloMinecraft26.2.exe`.
- Setup auto-extractible léger qui installe uniquement le GUI dans `%LOCALAPPDATA%\\LoloLauncher`.

Le mode hors-ligne utilise un profil local et un UUID déterministe. Un skin officiel
Mojang nécessite une connexion Microsoft ; le choix PNG intégré est donc un stockage
local compatible avec ce mode hors-ligne, pas une modification du compte Mojang.

## Build

Depuis PowerShell :

```powershell
.\build.ps1
.\build-setup.ps1
```

Le setup final est `D:\Code 2\LoloLauncherSetup.exe`. Il crée les raccourcis
Bureau et menu Démarrer, puis ouvre le GUI installé. Minecraft est téléchargé
au premier lancement. `--no-launch` permet de tester uniquement l'installation.

L'application WinUI 3 est lancée depuis `D:\Code 2\LoloMinecraftGUI`. Si un
`LoloMinecraft26.2.exe` est placé à côté, il sera utilisé ; sinon le GUI installe
Minecraft à la demande dans `.lolo-mc`.

Les versions téléchargées, les bibliothèques, les assets et les profils Fabric sont
stockés uniquement dans `%APPDATA%\\.lolo-mc`. Les binaires Minecraft restent exclus
du dépôt Git.

Le dépôt contient uniquement les sources et scripts de build. Les binaires Minecraft ne sont pas versionnés.
