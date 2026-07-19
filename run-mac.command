#!/bin/zsh
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="/opt/homebrew/opt/dotnet/libexec:$PATH"
fi

export DOTNET_CLI_HOME="${TMPDIR:-/tmp}/pk3ds-dotnet"
exec dotnet run --project "$SCRIPT_DIR/pk3DS.Mac.Web/pk3DS.Mac.Web.csproj"
