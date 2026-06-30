namespace RhMcp;

// Which concrete CliAgent a definition maps to. Claude is first so the default ordering
// reads naturally; custom entries surface this to alias a built-in adapter at a new path.
internal enum AgentAdapter
{
    Claude,
    Codex,
    Gemini,
}
