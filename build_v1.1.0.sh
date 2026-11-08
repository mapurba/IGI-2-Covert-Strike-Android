#!/bin/bash
#
# Build script for IGI-2 Custom Mission v1.2.0
# Builds terrain prototype with Phase 4 gameplay objects
# Phase 4: Gameplay implementation (guards, objectives, extraction)
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/app/android/build/logs"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
LOG_FILE="$BUILD_DIR/build-v1.2.0-$TIMESTAMP.log"
TARGET_VERSION="1.2.0"

mkdir -p "$BUILD_DIR"

echo "========================================" | tee "$LOG_FILE"
echo "IGI-2 Custom Mission Build v$TARGET_VERSION" | tee -a "$LOG_FILE"
echo "Phase 4: Gameplay Implementation (Guards, Objectives, Extraction)" | tee -a "$LOG_FILE"
echo "Timestamp: $TIMESTAMP" | tee -a "$LOG_FILE"
echo "========================================" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Phase 1: Build and test terrain prototype
echo "[Phase 1] Building terrain prototype with gameplay..." | tee -a "$LOG_FILE"
cd "$SCRIPT_DIR"

# Run self-test
echo "  - Running self-tests..." | tee -a "$LOG_FILE"
dotnet run --project apps/ProjectIGI.TerrainPrototype -- --self-test 2>&1 | tee -a "$LOG_FILE"
if [ $? -ne 0 ]; then
    echo "ERROR: Terrain prototype self-test failed!" | tee -a "$LOG_FILE"
    exit 1
fi
echo "  ✓ Self-tests passed (includes Phase 4 gameplay objects)" | tee -a "$LOG_FILE"

# Generate prototype outputs
PROTOTYPE_OUT="/tmp/long-descent-v$TARGET_VERSION-$TIMESTAMP"
echo "  - Generating outputs to: $PROTOTYPE_OUT" | tee -a "$LOG_FILE"
rm -rf "$PROTOTYPE_OUT"
dotnet run --no-build --project apps/ProjectIGI.TerrainPrototype -- "$PROTOTYPE_OUT" 2>&1 | tee -a "$LOG_FILE"
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to generate prototype outputs!" | tee -a "$LOG_FILE"
    exit 1
fi

# Verify outputs
echo "  - Verifying outputs..." | tee -a "$LOG_FILE"
echo "    Files generated:" | tee -a "$LOG_FILE"
ls -lh "$PROTOTYPE_OUT"/ | tee -a "$LOG_FILE"

if [ ! -f "$PROTOTYPE_OUT/heightmaps.res" ]; then
    echo "ERROR: heightmaps.res not generated!" | tee -a "$LOG_FILE"
    exit 1
fi
if [ ! -f "$PROTOTYPE_OUT/mission.qvm" ]; then
    echo "ERROR: mission.qvm not generated!" | tee -a "$LOG_FILE"
    exit 1
fi
if [ ! -f "$PROTOTYPE_OUT/objects.qvm" ]; then
    echo "ERROR: objects.qvm (Phase 4 gameplay) not generated!" | tee -a "$LOG_FILE"
    exit 1
fi

echo "  ✓ All prototype outputs generated successfully" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Show mission and objects content
echo "  - Mission script content (first 15 lines):" | tee -a "$LOG_FILE"
head -15 "$PROTOTYPE_OUT/mission.qvm" | sed 's/^/    /' | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

echo "  - Objects script preview (Phase 4 gameplay):" | tee -a "$LOG_FILE"
head -40 "$PROTOTYPE_OUT/objects.qvm" | sed 's/^/    /' | tee -a "$LOG_FILE"
echo "    [... additional guards and objectives ...]" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Phase 2: Verify APK version
echo "[Phase 2] Verifying APK version configuration..." | tee -a "$LOG_FILE"
VERSION_CODE=$(grep "versionCode" "$SCRIPT_DIR/app/android/app/build.gradle" | head -1 | awk '{print $2}')
VERSION_NAME=$(grep "versionName" "$SCRIPT_DIR/app/android/app/build.gradle" | head -1 | awk -F'"' '{print $2}')
echo "  - versionCode: $VERSION_CODE" | tee -a "$LOG_FILE"
echo "  - versionName: $VERSION_NAME" | tee -a "$LOG_FILE"

if [ "$VERSION_NAME" != "$TARGET_VERSION" ]; then
    echo "ERROR: APK version is not $TARGET_VERSION (found $VERSION_NAME)!" | tee -a "$LOG_FILE"
    exit 1
fi
echo "  ✓ Version is correctly set to $TARGET_VERSION" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Phase 3: Build APK
echo "[Phase 3] Building APK release..." | tee -a "$LOG_FILE"
echo "  - Using Java version:" | tee -a "$LOG_FILE"
java -version 2>&1 | sed 's/^/    /' | tee -a "$LOG_FILE"

cd "$SCRIPT_DIR/app/android"

echo "  - Cleaning previous build artifacts..." | tee -a "$LOG_FILE"
rm -rf .gradle build
bash ./gradlew --no-daemon --stop > /dev/null 2>&1 || true
sleep 2

echo "  - Starting Gradle build (this may take 5-15 minutes)..." | tee -a "$LOG_FILE"
if timeout 900 bash ./gradlew --no-daemon --info assembleRelease >> "$LOG_FILE" 2>&1; then
    echo "  ✓ APK build completed successfully" | tee -a "$LOG_FILE"
else
    BUILD_STATUS=$?
    if [ $BUILD_STATUS -eq 124 ]; then
        echo "ERROR: Build timed out after 15 minutes!" | tee -a "$LOG_FILE"
    else
        echo "ERROR: APK build failed with exit code $BUILD_STATUS!" | tee -a "$LOG_FILE"
    fi
    echo "  Build log: $LOG_FILE" | tee -a "$LOG_FILE"
    exit 1
fi

echo "" | tee -a "$LOG_FILE"

# Phase 4: Verify APK
echo "[Phase 4] Verifying generated APK..." | tee -a "$LOG_FILE"
APK_PATH="$SCRIPT_DIR/app/android/app/build/outputs/apk/release/app-release.apk"

if [ ! -f "$APK_PATH" ]; then
    echo "ERROR: APK file not found at $APK_PATH!" | tee -a "$LOG_FILE"
    exit 1
fi

APK_SIZE=$(ls -lh "$APK_PATH" | awk '{print $5}')
echo "  - APK location: $APK_PATH" | tee -a "$LOG_FILE"
echo "  - APK size: $APK_SIZE" | tee -a "$LOG_FILE"
echo "  ✓ APK file verified" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Summary
echo "========================================" | tee -a "$LOG_FILE"
echo "BUILD SUCCESSFUL - v$TARGET_VERSION" | tee -a "$LOG_FILE"
echo "========================================" | tee -a "$LOG_FILE"
echo "Prototype artifacts:" | tee -a "$LOG_FILE"
echo "  - Terrain: $PROTOTYPE_OUT/heightmaps.res ($( ls -lh "$PROTOTYPE_OUT/heightmaps.res" | awk '{print $5}' ))" | tee -a "$LOG_FILE"
echo "  - Preview: $PROTOTYPE_OUT/terrain-preview.pgm ($( ls -lh "$PROTOTYPE_OUT/terrain-preview.pgm" | awk '{print $5}' ))" | tee -a "$LOG_FILE"
echo "  - Mission: $PROTOTYPE_OUT/mission.qvm ($( ls -lh "$PROTOTYPE_OUT/mission.qvm" | awk '{print $5}' ))" | tee -a "$LOG_FILE"
echo "  - Objects (Phase 4): $PROTOTYPE_OUT/objects.qvm ($( ls -lh "$PROTOTYPE_OUT/objects.qvm" | awk '{print $5}' ))" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "APK artifact:" | tee -a "$LOG_FILE"
echo "  - Location: $APK_PATH" | tee -a "$LOG_FILE"
echo "  - Size: $APK_SIZE" | tee -a "$LOG_FILE"
echo "  - Version: v$VERSION_NAME (code $VERSION_CODE)" | tee -a "$LOG_FILE"
echo "  - Phase: Phase 4 Gameplay Implementation" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "Full log: $LOG_FILE" | tee -a "$LOG_FILE"
echo "========================================" | tee -a "$LOG_FILE"

exit 0
