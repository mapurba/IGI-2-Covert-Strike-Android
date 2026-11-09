#!/usr/bin/env bash
# Generate and validate the Long Descent prototype. This is not yet a playable APK.
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
OUTPUT_DIR="/tmp/long-descent-epic-1.3.0-${TIMESTAMP}"
LOG_DIR="${BUILD_LOG_DIR:-$ROOT_DIR/app/android/build/logs}"
LOG_FILE="${LOG_DIR}/phase4-epic-${TIMESTAMP}.log"
mkdir -p "$LOG_DIR"
exec > >(tee -a "$LOG_FILE") 2>&1

on_error() {
    local status=$?
    printf 'ERROR: Prototype generation failed with exit %d at line %s.\n' \
        "$status" "${BASH_LINENO[0]:-unknown}" >&2
    printf 'Log: %s\n' "$LOG_FILE" >&2
}
trap on_error ERR

command -v dotnet >/dev/null 2>&1 || {
    echo "ERROR: .NET SDK not found." >&2
    exit 1
}

printf 'Generating mission prototype in %s\n' "$OUTPUT_DIR"
dotnet run --project apps/ProjectIGI.TerrainPrototype -- "$OUTPUT_DIR"
dotnet run --project apps/ProjectIGI.TerrainPrototype -- --self-test

printf '\nERROR: Mission 99 is not deployable: objects.prototype.txt is a design sketch, not a compiled playable level.\n' >&2
printf 'No mission assets were copied and no APK was built. Use bash build_android.sh to build the existing app.\n' >&2
printf 'Prototype: %s\nLog: %s\n' "$OUTPUT_DIR" "$LOG_FILE" >&2
exit 1
