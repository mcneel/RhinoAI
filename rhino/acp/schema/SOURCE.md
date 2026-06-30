# Pinned ACP schema source

`schema.json` and `meta.json` are vendored verbatim from the official Agent Client Protocol repo.

- **Repo:** https://github.com/zed-industries/agent-client-protocol
- **Tag:** `v0.9.1`
- **Commit:** `e23620fe29cb24555db8fb8b58b641b680788e5f`
- **Wire protocol version:** `1` (`meta.json` `version`)

## Updating

1. Bump the tag above and re-download both files at that ref:
   ```
   TAG=vX.Y.Z
   curl -fsSL "https://raw.githubusercontent.com/zed-industries/agent-client-protocol/$TAG/schema/schema.json" -o rhino/acp/schema/schema.json
   curl -fsSL "https://raw.githubusercontent.com/zed-industries/agent-client-protocol/$TAG/schema/meta.json"   -o rhino/acp/schema/meta.json
   ```
2. Regenerate the C#: `dotnet run --project rhino/acp/codegen`
3. Build + test: `dotnet test tests/Acp.Tests`
4. Review the `src/Generated/` diff before committing.
