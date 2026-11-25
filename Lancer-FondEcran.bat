@echo off
if exist "%~dp0dist\EasyWallpaper.exe" (
    start "" "%~dp0dist\EasyWallpaper.exe"
) else (
    start "" "%~dp0bin\Release\net10.0-windows\EasyWallpaper.exe"
)
