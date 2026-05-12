#!/bin/bash
# Locate rhino-mcp-router inside an installed Rhino-MCP-Platform yak.
# Layout: ~/Library/Application Support/McNeel/Rhinoceros/packages/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/router/<rid>/rhino-mcp-router
# Mac currently ships only osx-arm64; the path mirrors the Windows layout so the
# launchers stay symmetric.

set -e

case "$(uname -m)" in
  arm64|aarch64) rid="osx-arm64" ;;
  *) rid="osx-arm64" ;;
esac

PKGROOT="$HOME/Library/Application Support/McNeel/Rhinoceros/packages"

for ver in 9.0 8.0; do
  base="$PKGROOT/$ver/Rhino-MCP-Platform"
  [ -d "$base" ] || continue

  while IFS= read -r pkgver; do
    cand="$base/$pkgver/router/$rid/rhino-mcp-router"
    if [ -x "$cand" ]; then
      exec "$cand" "$@"
    fi
  done < <(ls -1 "$base" 2>/dev/null | sort -r)
done

echo "Could not find rhino-mcp-router under $PKGROOT/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/router/$rid/." >&2
echo "Install Rhino-MCP-Platform via Rhino's PackageManager." >&2
exit 1
