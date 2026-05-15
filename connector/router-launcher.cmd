@echo off
REM Locate rhino-mcp-router.exe inside an installed Rhino-MCP-Platform yak.
REM Layout: %APPDATA%\McNeel\Rhinoceros\packages\<rhino-ver>\Rhino-MCP-Platform\<pkg-ver>\router\<rid>\rhino-mcp-router.exe
REM Yak tags packages by OS but not by CPU arch, so the Windows yak bundles both
REM win-x64 and win-arm64 router binaries side-by-side; this script picks at runtime.
REM Forwards all args (e.g. --default-version WIP) to the router.

setlocal enabledelayedexpansion

REM Detect arch. PROCESSOR_ARCHITEW6432 is set when a 32-bit process runs on a 64-bit OS.
set "RID=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
if /i "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "RID=win-arm64"

set "PKGROOT=%APPDATA%\McNeel\Rhinoceros\packages"
set "FOUND="

REM Walk Rhino versions newest-first. Router lives in a `router/<rid>/` subdir of
REM the package install dir to keep its deps isolated from the plugin's bundled
REM ASP.NET Core dlls (which target a different version family).
for %%V in (9.0 8.0) do (
    if exist "!PKGROOT!\%%V\Rhino-MCP-Platform" (
        REM Pick the newest package version subdir (lexical order — yak uses semver).
        for /f "delims=" %%P in ('dir /b /a:d /o:-n "!PKGROOT!\%%V\Rhino-MCP-Platform" 2^>nul') do (
            set "CAND=!PKGROOT!\%%V\Rhino-MCP-Platform\%%P\router\!RID!\rhino-mcp-router.exe"
            if exist "!CAND!" (
                set "FOUND=!CAND!"
                goto :found
            )
        )
    )
)

echo Could not find rhino-mcp-router.exe under !PKGROOT!\^<rhino-ver^>\Rhino-MCP-Platform\^<pkg-ver^>\router\!RID!\. >&2
echo Install Rhino-MCP-Platform via Rhino's PackageManager. >&2
exit /b 1

:found
"!FOUND!" %*
endlocal
