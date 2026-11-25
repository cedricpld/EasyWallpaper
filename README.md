# EasyWallpaper

Gestionnaire de fonds d'écran multi-moniteurs pour Windows 11. Permet d'assigner indépendamment à chaque écran une image, une vidéo locale, une webcam ou un flux vidéo en direct, avec aperçu fidèle et recadrage interactif.

---

## Fonctionnalités

- **Agencement réel des écrans** : détection automatique de tous les moniteurs connectés (résolution, orientation portrait/paysage, position relative) et affichage proportionnel dans l'interface.
- **Médias supportés par écran** :
  - Images locales (`JPG`, `PNG`, `BMP`, `WebP`, `GIF`) par clic ou glisser-déposer.
  - Vidéos locales en boucle (`MP4`, `WebM`, `MKV`, `MOV`, `AVI`, `WMV`), son coupé par défaut.
  - Flux vidéo en direct temps réel : flux HLS (`.m3u8`), flux vidéo directs, YouTube / YouTube Live, ou pages web.
  - Webcams par image/snapshot (`JPG`, `MJPEG`) avec intervalle de rafraîchissement configurable (`5 s` à `5 min`).
- **Ajustement et recadrage indépendants** :
  - Modes `Remplir`, `Ajuster`, `Étirer` et `Centrer` configurables individuellement pour chaque écran.
  - Aperçu en temps réel au ratio exact de chaque moniteur.
  - Déplacement à la souris (cliquer-glisser) directement dans la preview pour choisir la zone visible lorsque l'image ou le flux est recadré.
- **Intégration système** :
  - Exécutable autonome (`EasyWallpaper.exe`) avec icône embarquée.
  - Icône permanente dans la zone de notification (Tray) : `Ouvrir`, `Lancer au démarrage`, `Redémarrer`, `Quitter`.
  - Sauvegarde automatique de la configuration dans `%LOCALAPPDATA%\EasyWallpaper\config.json` et restauration automatique à chaque lancement.

---

## Utilisation et compilation

### Prérequis

- Windows 10 / 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (et [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) pour compiler depuis les sources)
- Microsoft Edge WebView2 Runtime (préinstallé sur Windows 11)

### Compiler en `.exe` autonome

Exécutez le script `Build-EasyWallpaper.bat` ou lancez manuellement :

```bat
dotnet publish CGWallpaper.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

L'exécutable généré se trouve dans `dist\EasyWallpaper.exe`. Il peut être déplacé et exécuté seul n'importe où.

Pour lancer l'application minimisée dans le Tray (utilisé par le démarrage automatique Windows) :

```bat
EasyWallpaper.exe --minimized
```

---

## Fonctionnement technique

### 1. Détection des écrans et DPI

L'énumération des moniteurs passe par l'interface COM native `IDesktopWallpaper` (`GetMonitorDevicePathCount`, `GetMonitorDevicePathAt`, `GetMonitorRect`) afin d'obtenir les identifiants matériels et les rectangles virtuels exacts de chaque écran. Le manifeste `app.manifest` active le mode `PerMonitorV2` pour garantir un positionnement au pixel près sur des configurations multi-écrans hétérogènes.

### 2. Rendu derrière les icônes du bureau (Windows 11 24H2 / 26H2)

Plutôt que de modifier le fond d'écran via `IDesktopWallpaper::SetWallpaper` ou `SystemParametersInfo` (qui peuvent être désactivés sur des sessions Windows non activées ou restreintes par stratégie), EasyWallpaper injecte ses propres fenêtres hôtes sans bordure (`WallpaperHostForm`, basées sur WinForms) dans la hiérarchie du bureau Windows :

1. Envoi du message `0x052C` à `Progman` (`SendMessageTimeout`) pour initialiser les couches du bureau.
2. Détection de la structure du compositeur :
   - Sur Windows 10 / 11 antérieur à 24H2 : rattachement au `WorkerW` de premier niveau situé après `SHELLDLL_DefView`.
   - Sur Windows 11 24H2 / 26H2 (*Raised Desktop*) : rattachement direct à `Progman` avec les styles Win32 `0x56010000` (`WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_TABSTOP`) et `0x08090080` (`WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_CONTROLPARENT | WS_EX_TOOLWINDOW`) + `SetLayeredWindowAttributes(..., 255, LWA_ALPHA)`.
3. Placement dans le Z-order juste derrière `SHELLDLL_DefView` (la couche des icônes) et maintien des fenêtres `WorkerW` enfants en bas de pile (`HWND_BOTTOM`) pour éviter qu'un écran ne recouvre l'autre.
4. Abonnement à `SystemEvents.DisplaySettingsChanged` pour ré-attacher automatiquement les fenêtres après un changement de résolution ou une sortie de veille.

### 3. Lecture des médias et flux temps réel

- **Images** : chargées en mémoire (`Bitmap`) et dessinées dans `OnPaint` avec `InterpolationMode.HighQualityBicubic`.
- **Vidéos, flux `.m3u8` et pages web** : rendus via `Microsoft.Web.WebView2` (moteur Chromium/Edge). Les flux HLS (`.m3u8`) utilisent `hls.js` en mode faible latence avec reprise automatique sur erreur réseau/média.
- **Recadrage (`OffsetX`, `OffsetY`)** : deux valeurs normalisées entre `0.0` et `1.0` (`0.5` par défaut) calculent le décalage en pixels pour les images GDI+ et sont converties en pourcentages CSS `object-position` (en `CultureInfo.InvariantCulture`) pour les flux HTML5/HLS.

### 4. Persistance et démarrage Windows

La configuration de chaque écran (`MonitorId`, type de source, URL/chemin, intervalle de rafraîchissement, mode d'ajustement, `OffsetX`/`OffsetY`) est sérialisée en JSON dans `%LOCALAPPDATA%\EasyWallpaper\config.json`. L'option de démarrage automatique ajoute ou supprime une entrée `EasyWallpaper` pointant vers `"<chemin>\EasyWallpaper.exe" --minimized` dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
