#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANDROID_DIR="$ROOT_DIR/sarmat-crew"

info() { printf '\n\033[1;36m%s\033[0m\n' "$*"; }
success() { printf '\033[1;32m%s\033[0m\n' "$*"; }
fail() { printf '\033[1;31mError: %s\033[0m\n' "$*" >&2; exit 1; }

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "required command '$1' was not found."
}

run_web() {
    require_command npm
    cd "$ROOT_DIR"
    npm "$@"
}

run_gradle() {
    [[ -x "$ANDROID_DIR/gradlew" ]] || fail "$ANDROID_DIR/gradlew was not found."
    cd "$ANDROID_DIR"
    ./gradlew "$@"
}

show_android_artifacts() {
    local variant="$1"
    local found=0
    while IFS= read -r artifact; do
        [[ -n "$artifact" ]] || continue
        printf '  %s\n' "$artifact"
        found=1
    done < <(find "$ANDROID_DIR/app/build/outputs" -type f -path "*/$variant/*" \( -name '*.apk' -o -name '*.aab' \) 2>/dev/null | sort)
    [[ "$found" -eq 1 ]] || printf '  No artifacts found.\n'
}

command_dev() {
    info "Starting Monitor in development mode (frontend + backend)..."
    run_web run dev
}

command_build() {
    info "Building Monitor for production..."
    run_web run build
    success "Monitor production build completed."
}

command_android_release() {
    info "Building Android release APK and AAB..."
    run_gradle :app:assembleRelease :app:bundleRelease
    success "Android release build completed:"
    show_android_artifacts release
    printf '\nNote: release APK/AAB artifacts are unsigned unless a signingConfig is configured.\n'
}

command_android_debug() {
    info "Building Android debug APK..."
    run_gradle :app:assembleDebug
    success "Android debug build completed:"
    show_android_artifacts debug
}

command_android_install() {
    local serial="${1:-}"
    info "Building, installing, and launching the Android debug app..."
    if [[ -n "$serial" ]]; then
        "$ANDROID_DIR/build-and-install.sh" "$serial"
    else
        "$ANDROID_DIR/build-and-install.sh"
    fi
}

command_test() {
    info "Running Monitor tests..."
    run_web test
    info "Running Android unit tests..."
    run_gradle :app:testDebugUnitTest
    success "All tests completed."
}

command_typecheck() {
    info "Running TypeScript checks..."
    run_web run typecheck
    success "TypeScript checks completed."
}

command_db_migrate() {
    info "Applying database migrations..."
    run_web run db:migrate
    success "Database migrations applied."
}

command_db_seed() {
    info "Seeding the database..."
    run_web run db:seed
    success "Database seed completed."
}

command_install_dependencies() {
    info "Installing Node.js dependencies..."
    require_command npm
    cd "$ROOT_DIR"
    npm ci
    success "Dependencies installed."
}

show_help() {
    cat <<'EOF'
Sarmat launcher

Usage:
  ./launcher.sh                         interactive menu
  ./launcher.sh dev                     development frontend + backend
  ./launcher.sh build                   Monitor production build
  ./launcher.sh android-release         release APK + AAB
  ./launcher.sh android-debug           debug APK
  ./launcher.sh android-install [serial] debug APK + install + launch
  ./launcher.sh test                    all Monitor and Android tests
  ./launcher.sh typecheck               TypeScript typecheck
  ./launcher.sh db-migrate              database migrations
  ./launcher.sh db-seed                 seed the database
  ./launcher.sh install                 npm ci
  ./launcher.sh help                    show this help
EOF
}

run_command() {
    local command="${1:-menu}"
    shift || true
    case "$command" in
        dev) command_dev ;;
        build) command_build ;;
        android-release|release) command_android_release ;;
        android-debug|debug) command_android_debug ;;
        android-install|install-android) command_android_install "${1:-}" ;;
        test|tests) command_test ;;
        typecheck|check) command_typecheck ;;
        db-migrate|migrate) command_db_migrate ;;
        db-seed|seed) command_db_seed ;;
        install) command_install_dependencies ;;
        help|-h|--help) show_help ;;
        *) fail "unknown command '$command'. Run ./launcher.sh help." ;;
    esac
}

show_menu() {
    while true; do
        printf '\n\033[1;36mSARMAT - LAUNCHER\033[0m\n'
        cat <<'EOF'
  1. Start Monitor in development mode
  2. Build Monitor for production
  3. Build Android release (APK + AAB)
  4. Build and install Android debug
  5. Build Android debug APK
  6. Run all tests
  7. Run TypeScript checks
  8. Apply database migrations
  9. Seed the database
 10. Install Node.js dependencies
  0. Exit
EOF
        printf '\nSelect an action: '
        read -r choice
        case "$choice" in
            1) command_dev ;;
            2) command_build ;;
            3) command_android_release ;;
            4) command_android_install ;;
            5) command_android_debug ;;
            6) command_test ;;
            7) command_typecheck ;;
            8) command_db_migrate ;;
            9) command_db_seed ;;
            10) command_install_dependencies ;;
            0) exit 0 ;;
            *) printf 'Unknown option: %s\n' "$choice" >&2 ;;
        esac
    done
}

if [[ "$#" -eq 0 ]]; then
    show_menu
else
    run_command "$@"
fi
