@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Arret de EasyWallpaper si actif...
taskkill /IM EasyWallpaper.exe /F >nul 2>&1

echo [2/2] Compilation en .exe autonome...
dotnet publish "%~dp0EasyWallpaper.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0dist"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Erreur lors de la compilation !
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Compilation reussie : "%~dp0dist\EasyWallpaper.exe"
pause
