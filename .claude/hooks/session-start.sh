#!/bin/bash
# Prepares a fresh Claude Code cloud container so the build, the tests and the screenshot runner work on the first
# try: the .NET SDK global.json pins, restored packages, and Playwright pointed at the pre-installed Chromium.
# Cloud only, idempotent, quiet on success, and it never fails the session: problems are logged and it exits 0.
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

repo="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
log="${TMPDIR:-/tmp}/pofightjudge-session-start.log"
: > "$log"
warn() { echo "session-start: $*" | tee -a "$log" >&2; }

dotnet_root="$HOME/.dotnet"
export PATH="$dotnet_root:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 HUSKY=0
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export PATH=\"$dotnet_root:\$PATH\""
    echo 'export DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 HUSKY=0'
  } >> "$CLAUDE_ENV_FILE"
fi

# 1. The SDK global.json pins (10.0.x, rollForward latestFeature). Installed per user, so no root needed.
channel=$(sed -nE 's/.*"version": *"([0-9]+\.[0-9]+)\..*/\1/p' "$repo/global.json" | head -1)
if ! dotnet --list-sdks 2>/dev/null | grep -q "^${channel:-10.0}\."; then
  installer="$(mktemp)"
  if curl -sSL https://dot.net/v1/dotnet-install.sh -o "$installer" \
    && bash "$installer" --channel "${channel:-10.0}" --install-dir "$dotnet_root" >> "$log" 2>&1; then
    :
  else
    warn "could not install the .NET ${channel:-10.0} SDK; see $log"
  fi
  rm -f "$installer"
fi

# 2. Packages. Restore (not build) so the cached container keeps them without pinning stale output.
if command -v dotnet > /dev/null; then
  dotnet restore "$repo/PoFightJudge.slnx" >> "$log" 2>&1 || warn "dotnet restore failed; see $log"
fi

# 3. Chromium. The container ships one build under /opt/pw-browsers; Microsoft.Playwright looks for the revision in
#    its own browsers.json. Link the installed headless shell into the path it expects instead of downloading.
browsers="${PLAYWRIGHT_BROWSERS_PATH:-/opt/pw-browsers}"
package=$(ls -d "$HOME"/.nuget/packages/microsoft.playwright/*/ 2>/dev/null | sort -V | tail -1)
manifest="${package}.playwright/package/browsers.json"
installed=$(ls -d "$browsers"/chromium_headless_shell-*/chrome-linux/headless_shell 2>/dev/null | head -1)
if [ -f "$manifest" ] && [ -n "$installed" ]; then
  revision=$(python3 -c 'import json,sys; print(next(b["revision"] for b in json.load(open(sys.argv[1]))["browsers"] if b["name"] == "chromium-headless-shell"))' "$manifest" 2>> "$log")
  target="$browsers/chromium_headless_shell-$revision"
  if [ -n "$revision" ] && [ ! -e "$target/chrome-headless-shell-linux64/chrome-headless-shell" ]; then
    { mkdir -p "$target/chrome-headless-shell-linux64" \
      && ln -sf "$installed" "$target/chrome-headless-shell-linux64/chrome-headless-shell" \
      && touch "$target/INSTALLATION_COMPLETE" "$target/DEPENDENCIES_VALIDATED"; } 2>> "$log" \
      || warn "could not link Chromium into $target; see $log"
  fi
else
  warn "Playwright package or pre-installed Chromium not found; UI tests and screenshots will skip"
fi

exit 0
