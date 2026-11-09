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

## Mission 99: Operation Redemption

The mission menu registers **Operation Redemption**, set in the Uttarakhand sector of India. David Jones is inserted at dawn to recover the stolen Quantum Encryption Core from Server Building 03 before it can be transferred to a foreign syndicate.

1. Breach the chain-link perimeter and evade the watchtowers.
2. Reach Guard Post A, disable its generator and take the security keycard.
3. Enter Server Building 03, hack the vault terminal and secure the Core.
4. Exfiltrate to the canyon extraction. If any guard detects Jones and raises the alarm, the Core locks down and cannot be recovered.

The generated 500 x 500 metre terrain has a mountain ridge, a cleared HALO insertion pocket, a plateau compound, internal roads, perimeter fencing with two double-leaf gates, vegetation and a lake-side extraction. Twelve placed structures use retail models and interiors: Guard Post A, the main gate booth, three watchtowers, barracks, command building, vehicle depot, logistics warehouse, Server Building 03, communications bunker and armory. Fitted doors include open, pickable, keycard-controlled and terminal-controlled routes; the generator self-test verifies the gate and objective wiring.

The compound has six cameras, fifteen guards in two squads, eight walking patrol routes, mission status messages, a security alarm and the four-stage generator/keycard/terminal/Core interaction chain. The generator disables the outer cameras; hacking the vault terminal disables the remaining cameras and unlocks the Core interaction. The alarm permanently blocks Core recovery. The supplied briefing screen, WAV voice-over and captions are bundled with the level.

The generator self-test is a scripted/geometry check, not a complete gameplay certification. The debug APK was installed on the connected Android phone and the generated mission scripts were verified against the installed files; the level was launched and a real gameplay screenshot captured. A complete hand-played run through every door, the alarm fail-state and extraction has not been recorded.

The shared first-person renderer now preserves opaque alpha for baked weapons/items drawn directly to the display. Captured scene targets retain the zero-alpha rig marker used by scene effects, and translucent draws keep their original blend behavior. This fixes the black silhouette without replacing retail weapon textures or adding new weapons. An M16A2 texture was visibly restored on the test phone; the entire arsenal has not been visually checked.

The .NET 10 mission generator creates the scripts, 1024 x 1024 terrain archive, terrain preview, forest placement data, guard AI scripts, sky and ground textures, and briefing assets. Generate to a new directory, then run its self-test:

```bash
dotnet run --project apps/ProjectIGI.TerrainPrototype -- /path/to/new-output-directory
dotnet run --project apps/ProjectIGI.TerrainPrototype -- --self-test
```

The output directory must not already exist. The generated terrain reuses the imported game's ground material and the building models, interiors, door models and props come from the user's retail packs; copyrighted retail assets are not bundled in the APK. Android installs the mission under the existing `Project IGI 2/MISSIONS/long_descent` directory, preserving the retail campaign and backing up recognized previous Mission 99 revisions before updating. Unknown or conflicting mission data is not overwritten.

To identify possible interior models in those installed packs without copying game assets, run `dotnet run --project apps/ProjectIGI.TerrainPrototype -- --inspect-buildings "<game-directory>"`. The listing reports render bounds, shell portals and attached models with portal sectors. It does not resolve magic-object types or establish walkable entrances, stairs, collision, furnishings or gameplay; candidates must be checked in the game before use.

`-- --walk-test "<game-directory>"` loads the placed building and door collision models from the installed packs and samples doorway clearance and reachable floor space. Set `IGI_CELL=0.2` to resolve narrow retail staircases. This is a geometric diagnostic, not a substitute for testing movement and interactions with the in-game player physics.

Fifteen guards use the existing soldier AI system; eight patrol nodes define their walking routes. Both squads link to the new alarm control, which responds to guard detection and sends the alarm event to the AI. The self-test checks task wiring and patrol graph data; a complete in-game AI behavior review remains outstanding.

The build is installed and running on the connected phone, and its Mission 99 scripts match the bundled hashes. The captured view confirms the compound, chain-link perimeter, roads, vegetation and HUD load; it also exposes remaining visual issues: the sky still renders as a flat brown field and foreground foliage partly frames the view. Buildings are retail-derived models rather than custom architecture, and not every requested detail (moving vehicles, bespoke rooftop/ventilation routes, all building-specific interiors and multi-species plants) is implemented. No full phone playthrough has yet verified the alarm lockdown, doors, terminal, Core recovery or extraction. See [the mission brief and phone capture](custom-mission/mission1.md) for the current assets and evidence.

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
