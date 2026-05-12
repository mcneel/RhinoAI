@echo off
REM Locate rhino-mcp-router.exe inside an installed Rhino-MCP-Platform yak.
REM Yak install paths are versioned per Rhino major version, so we walk newest-first.
REM Forwards all args (e.g. --default-version WIP) to the router.

setlocal enabledelayedexpansion

set "FOUND="
for %%V in (9.0 8.0) do (
    set "CAND=%APPDATA%\McNeel\Rhinoceros\packages\%%V\Rhino-MCP-Platform\rhino-mcp-router.exe"
    if exist "!CAND!" (
        set "FOUND=!CAND!"
        goto :found
    )
)

echo Could not find rhino-mcp-router.exe in any Rhino yak install. >&2
echo Install Rhino-MCP-Platform via Rhino's PackageManager. >&2
exit /b 1

:found
"!FOUND!" %*
endlocal
