#!/bin/bash
# Deploy "The Long Descent" mission to Android device
# Usage: ./deploy_mission.sh [mission_files_path]

MISSION_DIR="${1:-.}"
MISSION_ID="location9/level9"

echo "╔════════════════════════════════════════════════════════════╗"
echo "║   Deploying 'The Long Descent' Mission to Android Device   ║"
echo "╚════════════════════════════════════════════════════════════╝"
echo ""

# Check if adb is available
if ! command -v adb &> /dev/null; then
    echo "❌ ERROR: adb (Android Debug Bridge) not found!"
    echo "   Install Android SDK Platform Tools to continue"
    exit 1
fi

# Check if mission files exist
if [ ! -f "$MISSION_DIR/mission.qvm" ]; then
    echo "❌ ERROR: mission.qvm not found in $MISSION_DIR"
    exit 1
fi

if [ ! -f "$MISSION_DIR/objects.qvm" ]; then
    echo "❌ ERROR: objects.qvm not found in $MISSION_DIR"
    exit 1
fi

if [ ! -f "$MISSION_DIR/heightmaps.res" ]; then
    echo "❌ ERROR: heightmaps.res not found in $MISSION_DIR"
    exit 1
fi

echo "[1/4] Checking ADB connection..."
if ! adb devices | grep -q "device$"; then
    echo "❌ No Android device connected!"
    echo "   Connect your device and enable USB debugging, then try again"
    exit 1
fi

DEVICE=$(adb devices | grep "device$" | head -1 | awk '{print $1}')
echo "✓ Connected to device: $DEVICE"
echo ""

echo "[2/4] Creating mission directory on device..."
adb shell mkdir -p "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID"
if [ $? -ne 0 ]; then
    echo "❌ Failed to create directory on device"
    exit 1
fi
echo "✓ Directory created"
echo ""

echo "[3/4] Pushing mission files to device..."
echo "  → mission.qvm"
adb push "$MISSION_DIR/mission.qvm" "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID/" > /dev/null
if [ $? -ne 0 ]; then
    echo "❌ Failed to push mission.qvm"
    exit 1
fi

echo "  → objects.qvm"
adb push "$MISSION_DIR/objects.qvm" "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID/" > /dev/null
if [ $? -ne 0 ]; then
    echo "❌ Failed to push objects.qvm"
    exit 1
fi

echo "  → Creating level directory..."
adb shell mkdir -p "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID/level"

echo "  → heightmaps.res"
adb push "$MISSION_DIR/heightmaps.res" "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID/level/" > /dev/null
if [ $? -ne 0 ]; then
    echo "❌ Failed to push heightmaps.res"
    exit 1
fi

echo "✓ All mission files deployed"
echo ""

echo "[4/4] Verifying files on device..."
adb shell ls -lh "/sdcard/Documents/ProjectIGI2/MISSIONS/$MISSION_ID/" | grep -E "mission|objects"
if [ $? -eq 0 ]; then
    echo "✓ Verification successful"
else
    echo "⚠ Warning: Could not verify files"
fi

echo ""
echo "╔════════════════════════════════════════════════════════════╗"
echo "║ ✅ MISSION DEPLOYMENT COMPLETE                             ║"
echo "╠════════════════════════════════════════════════════════════╣"
echo "║ Next steps:                                                ║"
echo "║ 1. Close the game if it's running                         ║"
echo "║ 2. Restart ProjectIGI 2                                   ║"
echo "║ 3. Go to Campaign screen                                  ║"
echo "║ 4. Look for \"Mission 99: The Long Descent\"               ║"
echo "║                                                            ║"
echo "║ Mission Location:                                          ║"
echo "║ /sdcard/Documents/ProjectIGI2/MISSIONS/location9/level9/  ║"
echo "╚════════════════════════════════════════════════════════════╝"

exit 0
