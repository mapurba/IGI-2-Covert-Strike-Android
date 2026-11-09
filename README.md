# I.G.I. 2: Covert Strike (Native Android Engine)

A ground-up, high-performance native Android port of the classic tactical stealth first-person shooter **I.G.I. 2: Covert Strike** (2003).

This project runs the game natively on Android ARM64 devices (`com.projectigi2.app`) without emulation or compatibility layers, combining an embedded **.NET 10.0 CoreCLR** runtime, **Vulkan 1.1** graphics, and **OpenAL Soft** spatial audio with authentic retail game logic, assets, and QVM menu system.

---

## User Instructions (How to Play)

For copyright compliance, this APK contains **no copyrighted game data**. To play the game:

1. **Install the APK** on your Android device (ARM64 device running Android 7.0+).
2. **Copy your PC Game Folder** to your Android device storage (e.g., in `/sdcard/Download/IGI 2`). Your game directory must be the installation's **`pc` folder** — the one that contains `igi2.exe` alongside the original PC game folders (`common/`, `missions/`, `menusystem/`, `language/`, `config.qvm`).
3. **Launch I.G.I. 2** on your device.
4. On the launcher splash screen, tap **`SELECT GAME FOLDER`**.
5. Select your copied `pc` folder using Android's system file picker.
6. The app will automatically verify the game file structure and enable the **`PLAY`** button.
7. Tap **`PLAY`** to launch into the game!

---

#
## Architecture: How It's Made & How It Runs

```
┌─────────────────────────────────────────────────────────────┐
│                    Android Application                      │
│            (MainActivity.java / SurfaceView)                │
└──────────────────────────────┬──────────────────────────────┘
                               │ JNI (ANativeWindow)
┌──────────────────────────────▼──────────────────────────────┐
│                    Native Bridge (C++)                      │
│      (libprojectigi.so + libopenal.so via OpenSL ES)        │
└──────────────────────────────┬──────────────────────────────┘
                               │ coreclr_initialize / delegates
┌──────────────────────────────▼──────────────────────────────┐
│             Embedded .NET 10.0 ARM64 Runtime                │
│                (ProjectIGI.Android.dll)                     │
├──────────────────────────────┬──────────────────────────────┤
│ Vulkan 1.1 Render Pipeline   │ OpenAL Soft 3D Audio Engine  │
│ Multi-Touch Input System     │ Pure C# VFS & IFF Decoders   │
│ Authentic I.G.I. 2 AI Engine │ Retail QVM Virtual Machine   │
└──────────────────────────────┴──────────────────────────────┘
```

1. **Android Shell (`app/android`)**:
   - `MainActivity.java` creates an immersive fullscreen `SurfaceView` and hands its `ANativeWindow` handle down to native C++ via JNI.
   - Includes a standalone SAF game folder selector and verifier to load external authentic game files. It verifies the I.G.I. 2 installation (signature file `igi2.exe`, data root is the `pc` folder).

2. **Native Layer (`app/android/app/src/main/cpp`)**:
   - `jni_bridge.cpp` boots the embedded .NET CoreCLR host, passes display parameters, and registers low-latency touch and keyboard callbacks.
   - Compiled with symbol obfuscation (`-fvisibility=hidden -Wl,--exclude-libs,ALL`) to strip internal NDK function signatures.
   - Integrates **OpenAL Soft 1.23.1** compiled natively for Android ARM64 with the `OpenSL ES` backend for low-latency 3D positional audio.

3. **Engine Core (`src/`)**:
   - **`ProjectIGI.Rendering.Vulkan`**: Low-overhead Vulkan 1.1 renderer utilizing ANativeWindow surfaceKHR, optimized forward rendering, baked lightmaps, and high-performance 2D sprite batching.
   - **`ProjectIGI.Game.Igi2`**: Re-implementation of I.G.I. 2 game logic — guard AI perception/patrol paths, squads, alarms, weapon ballistics, bullet penetration, inventory, and health mechanics.
   - **`ProjectIGI.Presentation.Igi2`**: Executes original `mainmenu.qvm` bytecode directly, reproducing the authentic retail menu, mission selector, and debriefing screens.
   - **`ProjectIGI.Audio.OpenAL`**: Drives dynamic environmental reverb, directional footsteps, alarms, and weapon acoustics.

The Android host (`AndroidHost.cs`) runs the I.G.I. 2 title through `TitleCatalog.For(GameTitle.Igi2)`. The engine's title system is shared with the I.G.I. 1 port — the same host, renderer and input stack, pointed at a different game.

---

## Mission 99: The Long Descent

The Android build includes an experimental Mission 99 world prototype. With the .NET 10 SDK, generate its assets into a new directory and run the asset self-test:

```bash
dotnet run --project apps/ProjectIGI.TerrainPrototype -- /path/to/new-output-directory
dotnet run --project apps/ProjectIGI.TerrainPrototype -- --self-test
```

The output directory must not already exist. The tool generates a compiled mission definition and level script, a `heightmaps/heightmaps.res` archive with a 1024 x 1024 tile, a generated prototype ground texture, a grayscale `terrain-preview.pgm`, an AI graph, forest placement files, and scripts that invoke the installed game's default AI handlers. The terrain material instead references `MISSIONS/multiplayer/jungle/textures/lushgrass.jpg` from the user's installed game. That file must be present in the imported game data; if it is missing, the renderer logs a warning and the ground draws untextured. The 500 x 500 metre terrain has a mountain lookout in the northwest and lower open ground toward the southeast. The compound and navigation graph retain their internal spacing while moving closer to the lookout. The level declares a daytime sky, midday sun and ambient light, uses the original player and first-person arms model names, and requests an M16A2. It also references the player's installed location 1 game packs for three compound buildings, 15 armed soldier placements, two camera placements, and tree, bush and grass models; those retail assets are not bundled in the APK. On Android, the app installs the mission under the existing `Project IGI 2/missions/long_descent` directory (using the existing folder's casing) after it verifies or imports the user's game files; no retail mission files are replaced. Exact matches to the earlier bundled prototypes are preserved in app storage before updating, and unrecognized mission files are not overwritten.

This is not a complete or playable extraction mission. The 500-metre revision loaded on the tested Android device at the expected lookout; the terrain boundary and sparse prototype compound were visibly exposed from that viewpoint. The existing three buildings have not been replaced by the requested accessible intelligence facility. The increased touch sensitivity while a weapon scope is active still needs device verification. Grass placements vary the size and rotation of one grass model rather than providing distinct grass species. Performance across the level has not been measured. The user reports that enemies fire. Guard patrols, taking damage and death remain unverified. Camera detection and alarms, convoy, navigable objective building, lake, intelligence retrieval, extraction, mission completion and failure remain unimplemented or unverified. The requested sniper loadout is not implemented. The first-person weapon appeared as a dark silhouette; the user reported that the controls and optics work. The preview shows height only. The self-test checks the generated graph's connectivity, vegetation and guard placement heights and scripts as well as terrain, daylight, task layout, delayed updates and weapon-script invocation; it does not establish AI behavior, camera detection, visual quality, or complete gameplay.

---

## Key Features & Current Build Highlights

- **Decoupled Game Files**: 100% clean repository and APK with no bundled copyrighted game data.
- **Optimized 30 FPS Target Pacing**: Frame loop pacer strictly enforces a **30 FPS** target frame limit by default for smooth battery-friendly mobile gameplay.
- **Authentic Retail Experience**: All 19 campaign missions, cutscenes, briefing audio, textures, weapon models, sound effects, and retail menu screens.
- **Responsive Touch HUD & Controls**:
  - Full multi-touch overlay with dual-stick movement, aiming, crouch, jump, reload, scope, and weapon selection.
  - **I.G.I. 2 stance buttons**: step the stance up (prone to crouch, crouch to stand) and down (stand to crouch, crouch to prone); prone is reached by tapping stance-down. Buttons can be dragged to reposition in the HUD customizer.
  - Interactive **Pause Button (`||`)** that instantly halts game simulation, AI, and level updates.
- **Clean Cutscene Audio & Skip Rules**:
  - Mission background music automatically pauses during cutscenes for crisp dialogue playback.
  - Interactive hold-to-skip ring for mid-mission cutscenes; post-mission completion cutscenes run unprompted for clean cinematic transitions.
- **Subtle Cheats Gear Icon**: A compact, unobtrusive gear icon on the main menu opens an on-demand modal overlay for:
  - **Infinite Health** (zero damage from bullets, falls, explosions, electric fences)
  - **Infinite Ammunition** (reserves never deplete)
  - **All Weapons** (unlocks full arsenal for the mission)

---

## Project Structure

```
.
├── app/
│   └── android/                      # Android Studio Gradle project
│       ├── app/                      # App module (JNI bridge, C++ CMake, game assets)
│       └── capacitor-android/        # Local Android bridge library
├── apps/
│   ├── ProjectIGI.Android/           # Native Android .NET CoreCLR entrypoint
│   └── ProjectIGI.Composition/       # Shared runtime composition & asset resolver
├── src/
│   ├── Audio/                        # OpenAL 3D sound subsystem
│   ├── Core/                         # VFS, IFF/QVM decoders, math, serialization
│   ├── Engine/                       # GameHost engine loop, HUD, touch input, cheats
│   ├── Game/                         # I.G.I. 2 game rules, AI, missions (and I.G.I. 1)
│   ├── Platform/                     # Abstract platform contracts, input state
│   ├── Rendering/                    # Vulkan 1.1 RHI, pipeline, shader batching
│   └── UI/                           # 2D UI painter, font rasterizers
├── Directory.Build.props             # Global .NET build configuration
├── Directory.Packages.props          # Central Package Management pins
└── global.json                       # .NET SDK pin
```

---

## Build Prerequisites

Ensure the following tools are installed and present in your `PATH`:

1. **.NET 10.0 SDK** (or newer)
2. **Android SDK** (API Level 34 or 35)
3. **Android NDK** (version `25.x` or `26.x` recommended)
4. **CMake** (3.22.1+) & **Ninja**
5. **Java Development Kit** (JDK 17)

---

## Step-by-Step Build Instructions

### One-command build (Linux/macOS)

From the repository root, run:

```bash
./build_android.sh
```

The script publishes and deploys the managed ARM64 assemblies, builds and verifies the release APK, and saves the full diagnostic log under `app/android/build/logs/`.

### 1. Build and Publish .NET ARM64 Assemblies

From the root of the repository:

```bash
dotnet publish apps/ProjectIGI.Android/ProjectIGI.Android.csproj -c Release -r android-arm64 --no-self-contained
```

Copy the compiled managed `.dll` and `.pdb` files into the Android project's asset directory. Use the deploy script — it does this for you (and the Android Studio build alone does **not** refresh the managed DLLs, so a stale build is the first thing to check when a fix seems missing):

**Windows (PowerShell):**
```powershell
.\deploy_managed_assets.ps1
```

**Linux / macOS:**
```bash
./deploy_managed_assets.sh
```

### 2. Build the Android Release APK

Navigate to the `app/android` directory and run Gradle:

**Windows:**
```cmd
cd app\android
gradlew.bat assembleRelease
```

**Linux / macOS:**
```bash
cd app/android
chmod +x gradlew
./gradlew assembleRelease
```

The generated APK will be located at:
`app/android/app/build/outputs/apk/release/app-release.apk`

---

## Credits & Disclaimer

- **Original Game**: Developed by **Innerloop Studios** and published by **Eidos Interactive** (2003).
- This project is an open-source educational recreation and engine rewrite intended for preservation and modern platform support.
- All trademarks and registered trademarks belong to their respective owners.
