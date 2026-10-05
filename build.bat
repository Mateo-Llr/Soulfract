@echo off
echo ===========================================
echo    COMPILATION - Soulfract
echo ===========================================

if not exist "Heroes.csproj" (
    echo [ERREUR] Lancez ce script depuis le dossier racine du projet.
    pause
    exit /b 1
)

echo [INFO] Nettoyage de l'ancienne compilation...
if exist "dist" (
    rmdir /s /q dist
)

echo.
echo [INFO] Publication en mode console pour permettre l'affichage de l'invite de debug...
echo.

dotnet publish Heroes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:OutputType=Exe -o ./dist

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERREUR] La publication a echoue.
    echo Verifiez que le SDK .NET 8 est bien installe.
    pause
    exit /b %ERRORLEVEL%
)

:: ===== NETTOYAGE DES FICHIERS INUTILES =====
echo [INFO] Nettoyage des fichiers residus (macOS, symboles, etc.)...
del /q .\dist\*.dylib 2>nul
del /q .\dist\*.pdb   2>nul
del /q .\dist\*.so    2>nul
del /q .\dist\*.xml   2>nul
:: ===========================================

echo.
echo ===========================================
echo    SUCCES - Fichier genere dans ./dist/
echo    Soulfract.exe est pret a etre lance.
echo ===========================================
echo.

echo [INFO] Lancement du jeu...
dotnet .\dist\Soulfract.dll

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERREUR] Le jeu s'est arrete avec un code d'erreur.
    pause
)