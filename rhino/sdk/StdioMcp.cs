using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

namespace Rhino.AI;

public sealed class StdioMcp : IMcp
{

    public string Name { get; }

    private Dictionary<string, ITool> PrivateTools { get; } = [];
    public IReadOnlyDictionary<string, ITool> Tools
    {
        get
        {
            if (PrivateTools.Count == 0) { Init(); }
            return PrivateTools;
        }
    }

    private Uri ProcessPath { get; }

    public StdioMcp(string name, Uri process)
    {
        Name = name;
        ProcessPath = process;
    }

    ~StdioMcp()
    {
        Process?.Close();
        Process?.Kill(true);
    }

    private StreamReader? Output => Process?.StandardOutput;
    private StreamReader? Error => Process?.StandardError;
    private StreamWriter? Input => Process?.StandardInput;

    private Process? Process { get; set; }

    public bool Init()
    {
        string path = ProcessPath.AbsolutePath;
        Process = new ()
        {
            StartInfo = new (path) { },
        };
        if (!Process.Start()) return false;

        // TODO : Send handskae via Input

        // TODO : Tools List

        return true;
    }

    public string RunTool(string ToolName, params (string Name, string Value)[] args)
    {
        // TODO :Run Tool
        return string.Empty;
    }

    public Task<bool> InitAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public void RegisterTool(ITool tool)
    {
        throw new NotImplementedException();
    }

    public Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
