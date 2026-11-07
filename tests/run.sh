#!/usr/bin/env bash
# Build everything (the Windows app compiles on Linux too) and run the tests. No Windows or Hearthstone needed.
# Test data lives in tests/data (not shipped):
#   tests/data/appdata/HearthStoner/cards.json, meta/*.json      (copies of %LocalAppData%\HearthStoner)
#   tests/data/install/Logs/Hearthstone_*/Power.log, Decks.log (copies of Hearthstone's Logs folder)
# On Windows just run: dotnet test HearthStoner.slnx   (uses your real data if tests/data isn't there)
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^10\.'; then
  if [ -x /opt/dotnet/dotnet ] && /opt/dotnet/dotnet --list-sdks | grep -q '^10\.'; then :
  else
    curl -sSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet >/dev/null
  fi
  export PATH=/opt/dotnet:$PATH DOTNET_ROOT=/opt/dotnet
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ -d tests/data ] || echo "note: no tests/data folder; tests that need cards.json or game logs will fail"
echo "=== build ==="
dotnet build HearthStoner.slnx -c Release -nologo -v q 2>&1 | grep -E " error | warning |Warning\(s\)|Error\(s\)|Build succeeded|FAILED" | sort -u
echo "=== test ==="
dotnet test HearthStoner.slnx -c Release --no-build -nologo "$@" 2>&1 | grep -vE "^\s*$|^Test run for|matched the specified pattern"
