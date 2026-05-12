#!/bin/bash
# Locate rhino-mcp-router inside an installed Rhino-MCP-Platform yak.
# Yak install paths are versioned per Rhino major version, so we walk newest-first.
# Forwards all args (e.g. --default-version WIP) to the router.

set -e

for ver in 9.0 8.0; do
  cand="$HOME/Library/Application Support/McNeel/Rhinoceros/packages/$ver/Rhino-MCP-Platform/rhino-mcp-router"
  if [ -x "$cand" ]; then
    exec "$cand" "$@"
  fi
done

echo "Could not find rhino-mcp-router in any Rhino yak install." >&2
echo "Install Rhino-MCP-Platform via Rhino's PackageManager." >&2
exit 1
