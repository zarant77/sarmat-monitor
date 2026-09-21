#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APK_PATH="$SCRIPT_DIR/app/build/outputs/apk/debug/app-debug.apk"
PACKAGE_NAME="com.sarmat.crew"
ACTIVITY_NAME="$PACKAGE_NAME/.MainActivity"

find_adb() {
    if command -v adb >/dev/null 2>&1; then
        command -v adb
        return
    fi

    local candidate
    for candidate in \
        "${ANDROID_SDK_ROOT:-}/platform-tools/adb" \
        "${ANDROID_HOME:-}/platform-tools/adb" \
        "${HOME:-}/Library/Android/sdk/platform-tools/adb"; do
        if [[ "$candidate" != "/platform-tools/adb" && -x "$candidate" ]]; then
            printf '%s\n' "$candidate"
            return
        fi
    done

    printf 'Помилка: adb не знайдено. Встановіть Android SDK Platform Tools.\n' >&2
    exit 1
}

REQUESTED_SERIAL="${1:-}"

if [[ "$REQUESTED_SERIAL" == "--help" || "$REQUESTED_SERIAL" == "-h" ]]; then
    printf 'Використання: %s [serial-пристрою]\n' "$(basename "$0")"
    printf 'Без serial скрипт автоматично вибере єдиний під’єднаний пристрій.\n'
    exit 0
fi

ADB="$(find_adb)"

printf 'Перевіряю Android-пристрій…\n'
"$ADB" start-server >/dev/null

if [[ -n "$REQUESTED_SERIAL" ]]; then
    DEVICE_STATE="$("$ADB" -s "$REQUESTED_SERIAL" get-state 2>/dev/null || true)"
    if [[ "$DEVICE_STATE" != "device" ]]; then
        printf 'Помилка: пристрій %s не під’єднаний або не авторизований.\n' "$REQUESTED_SERIAL" >&2
        "$ADB" devices -l >&2
        exit 1
    fi
    DEVICE_SERIAL="$REQUESTED_SERIAL"
else
    DEVICE_SERIALS="$("$ADB" devices | awk 'NR > 1 && $2 == "device" { print $1 }')"
    DEVICE_COUNT="$(printf '%s\n' "$DEVICE_SERIALS" | awk 'NF { count++ } END { print count + 0 }')"

    if [[ "$DEVICE_COUNT" -eq 0 ]]; then
        printf 'Помилка: немає авторизованого Android-пристрою.\n' >&2
        printf 'Увімкніть USB debugging, під’єднайте телефон і підтвердьте доступ.\n' >&2
        "$ADB" devices -l >&2
        exit 1
    fi

    if [[ "$DEVICE_COUNT" -gt 1 ]]; then
        printf 'Знайдено кілька пристроїв. Передайте serial одним аргументом:\n' >&2
        "$ADB" devices -l >&2
        printf 'Наприклад: %s DEVICE_SERIAL\n' "$(basename "$0")" >&2
        exit 1
    fi

    DEVICE_SERIAL="$DEVICE_SERIALS"
fi

printf 'Збираю debug APK…\n'
cd "$SCRIPT_DIR"
./gradlew :app:assembleDebug

if [[ ! -f "$APK_PATH" ]]; then
    printf 'Помилка: APK не знайдено: %s\n' "$APK_PATH" >&2
    exit 1
fi

printf 'Встановлюю Sarmat Crew на %s…\n' "$DEVICE_SERIAL"
"$ADB" -s "$DEVICE_SERIAL" install -r "$APK_PATH"

printf 'Запускаю Sarmat Crew…\n'
"$ADB" -s "$DEVICE_SERIAL" shell am start -n "$ACTIVITY_NAME" >/dev/null

printf 'Готово: Sarmat Crew зібрано, встановлено та запущено на %s.\n' "$DEVICE_SERIAL"
