#!/bin/bash
# Locate rhino-mcp-router inside an installed Rhino-MCP-Platform yak.
# Layout: ~/Library/Application Support/McNeel/Rhinoceros/packages/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/rhino-mcp-router
# We search across all Rhino versions and all package versions, newest-first.

set -e

PKGROOT="$HOME/Library/Application Support/McNeel/Rhinoceros/packages"

for ver in 9.0 8.0; do
  base="$PKGROOT/$ver/Rhino-MCP-Platform"
  [ -d "$base" ] || continue

  # Newest package version subdir first. Router lives in a `router/` subdir to keep
  # its deps isolated from the plugin's bundled ASP.NET Core dlls.
  while IFS= read -r pkgver; do
    cand="$base/$pkgver/router/rhino-mcp-router"
    if [ -x "$cand" ]; then
      exec "$cand" "$@"
    fi
  done < <(ls -1 "$base" 2>/dev/null | sort -r)
done

echo "Could not find rhino-mcp-router under $PKGROOT/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/." >&2
echo "Install Rhino-MCP-Platform via Rhino's PackageManager." >&2
exit 1
