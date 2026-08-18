#!/bin/zsh
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -x "/opt/homebrew/opt/dotnet/libexec/dotnet" ]; then
  export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
  export PATH="$DOTNET_ROOT:$PATH"
elif [ -x "/usr/local/opt/dotnet/libexec/dotnet" ]; then
  export DOTNET_ROOT="/usr/local/opt/dotnet/libexec"
  export PATH="$DOTNET_ROOT:$PATH"
fi
if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="/opt/homebrew/opt/dotnet/libexec:$PATH"
fi

DOTNET_VERSION="$(dotnet --version 2>/dev/null || true)"
case "$DOTNET_VERSION" in
  10.*) ;;
  *) echo "pk3DS Mac requiere .NET 10. SDK detectado: ${DOTNET_VERSION:-ninguno}" >&2; exit 1 ;;
esac

export DOTNET_CLI_HOME="${TMPDIR:-/tmp}/pk3ds-dotnet"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$SCRIPT_DIR/pk3DS.Mac.Web/frontend"
if [ ! -d node_modules ]; then npm install; fi
npm run build
cd "$SCRIPT_DIR"
exec dotnet run --project "$SCRIPT_DIR/pk3DS.Mac.Web/pk3DS.Mac.Web.csproj" -p:BuildInParallel=false
