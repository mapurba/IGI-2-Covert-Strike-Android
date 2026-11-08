#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

LOG_DIR="${BUILD_LOG_DIR:-$ROOT_DIR/app/android/build/logs}"
LOG_FILE="${BUILD_LOG:-$LOG_DIR/android-release-$(date '+%Y%m%d-%H%M%S').log}"
mkdir -p "$(dirname "$LOG_FILE")"
exec > >(tee -a "$LOG_FILE") 2>&1

on_error() {
    local status=$?
    printf '\nERROR: command failed with exit %d at line %s: %s\n' \
        "$status" "${BASH_LINENO[0]:-unknown}" "$BASH_COMMAND" >&2
    printf 'Full build log: %s\n' "$LOG_FILE" >&2
}
trap on_error ERR

fail() {
    printf 'ERROR: %s\n' "$1" >&2
    printf 'Full build log: %s\n' "$LOG_FILE" >&2
    exit 1
}

printf 'Android release build started: %s\n' "$(date --iso-8601=seconds)"
printf 'Repository: %s\nLog: %s\n\n' "$ROOT_DIR" "$LOG_FILE"

command -v dotnet >/dev/null 2>&1 || fail '.NET SDK is required but dotnet was not found on PATH.'
printf 'Using .NET SDK %s\n' "$(dotnet --version)"

java_candidates=()
if [[ -n "${JAVA_HOME:-}" ]]; then
    java_candidates+=("$JAVA_HOME")
fi
for candidate in /usr/lib/jvm/java-17-openjdk-* /usr/lib/jvm/jdk-17* \
    "$HOME"/.sdkman/candidates/java/17* /opt/java/jdk-17*; do
    if [[ -d "$candidate" ]]; then
        java_candidates+=("$candidate")
    fi
done

selected_java_home=''
for candidate in "${java_candidates[@]}"; do
    if [[ -x "$candidate/bin/java" ]] \
        && "$candidate/bin/java" -version 2>&1 | grep -qE 'version "17([.]|")'; then
        selected_java_home="$candidate"
        break
    fi
done
[[ -n "$selected_java_home" ]] || fail 'JDK 17 is required. Set JAVA_HOME to a JDK 17 installation.'
export JAVA_HOME="$selected_java_home"
export PATH="$JAVA_HOME/bin:$PATH"
printf 'Using JDK: %s\n' "$JAVA_HOME"
java -version 2>&1

SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Android/Sdk}}"
[[ -d "$SDK_ROOT" ]] || fail "Android SDK not found at $SDK_ROOT. Set ANDROID_SDK_ROOT to its location."
[[ -f "$SDK_ROOT/platforms/android-35/android.jar" ]] \
    || fail "Android SDK platform 35 is missing from $SDK_ROOT. Install platforms;android-35."
[[ -x "$SDK_ROOT/cmdline-tools/latest/bin/sdkmanager" ]] \
    || fail "Android command-line tools are missing from $SDK_ROOT/cmdline-tools/latest."
export ANDROID_HOME="$SDK_ROOT"
export ANDROID_SDK_ROOT="$SDK_ROOT"
printf 'Using Android SDK: %s\n\n' "$ANDROID_SDK_ROOT"

[[ -f "$ROOT_DIR/deploy_managed_assets.sh" ]] || fail 'deploy_managed_assets.sh was not found.'
[[ -f "$ROOT_DIR/app/android/gradlew" ]] || fail 'Android Gradle wrapper was not found.'

printf '=== Step 1/3: Publish and deploy managed ARM64 assemblies ===\n'
bash "$ROOT_DIR/deploy_managed_assets.sh"

printf '\n=== Step 2/3: Assemble signed Android release APK ===\n'
(
    cd "$ROOT_DIR/app/android"
    bash ./gradlew --info --stacktrace --console=plain assembleRelease
)

APK_PATH="$ROOT_DIR/app/android/app/build/outputs/apk/release/app-release.apk"
[[ -s "$APK_PATH" ]] || fail "Gradle completed but the APK was not found at $APK_PATH."

printf '\n=== Step 3/3: Verify APK artifact ===\n'
if [[ -x "$ANDROID_SDK_ROOT/build-tools/35.0.0/apksigner" ]]; then
    "$ANDROID_SDK_ROOT/build-tools/35.0.0/apksigner" verify "$APK_PATH"
fi
ls -lh "$APK_PATH"
printf '\nBUILD SUCCEEDED\nAPK: %s\nLog: %s\n' "$APK_PATH" "$LOG_FILE"