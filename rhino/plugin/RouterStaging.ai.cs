using System.IO;
using System.Runtime.InteropServices;

using Rhino.AI.Router;

namespace Rhino.AI;

internal record struct RouterStagingResult(string RouterPath, string? StagingError);

// Windows locks a running exe, so executing the installer-owned payload would block yak updates.
internal static class RouterStaging
{
    private const string TrashExtension = ".old";
    private const string TempExtension = ".staging";

    // router/ lives at the root of the plug-in folder, but the .rhp is not always at that
    // root: on macOS the output is flat (Plug-ins/RhinoAI/RhinoAI.rhp, router/ beside it),
    // on Windows the .rhp sits in a net8.0 subfolder with router/ one level up. Probe both
    // instead of assuming a layout; fall back to the sibling path so the error message from
    // EnsureStaged names a sensible location when neither exists.
    internal static string? PayloadDir
    {
        get
        {
            if (Path.GetDirectoryName(typeof(RouterStaging).Assembly.Location) is not string pluginDir)
                return null;

            string beside = Path.GetFullPath(Path.Combine(pluginDir, "router", Rid));
            if (Directory.Exists(beside))
                return beside;

            string above = Path.GetFullPath(Path.Combine(pluginDir, "..", "router", Rid));
            return Directory.Exists(above) ? above : beside;
        }
    }

    private static RouterStagingResult? Succeeded { get; set; }

    // Only successes are cached, so a staging failure is retried rather than frozen for the session.
    internal static RouterStagingResult EnsureStaged()
    {
        if (Succeeded is RouterStagingResult cached)
            return cached;

        RouterStagingResult result = PayloadDir is string payloadDir
            ? EnsureStaged(payloadDir, RouterPaths.BinDir)
            : new RouterStagingResult(RouterPaths.StagedRouterExe, "Could not locate the plug-in directory.");

        if (result.StagingError is null)
            Succeeded = result;

        return result;
    }

    internal static RouterStagingResult EnsureStaged(string payloadDir, string binDir)
    {
        string staged = Path.Combine(binDir, RouterPaths.RouterExeName);
        string payload = Path.Combine(payloadDir, RouterPaths.RouterExeName);

        try
        {
            Directory.CreateDirectory(binDir);
            SweepLeftovers(binDir);

            if (File.Exists(payload))
            {
                foreach (string source in PayloadFilesExeLast(payloadDir))
                    Replace(source, Path.Combine(binDir, RelativeToPayload(payloadDir, source)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RouterStagingResult(File.Exists(staged) ? staged : payload, ex.Message);
        }

        if (File.Exists(staged))
            return new RouterStagingResult(staged, null);

        return new RouterStagingResult(payload, $"No router payload found at {payload}.");
    }

    private static IEnumerable<string> PayloadFilesExeLast(string payloadDir)
    {
        string exe = Path.Combine(payloadDir, RouterPaths.RouterExeName);

        foreach (string file in Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(file, exe, StringComparison.OrdinalIgnoreCase))
                yield return file;
        }

        // Exe last, so a router spawned mid-update never pairs a new binary with an old sidecar.
        yield return exe;
    }

    private static string RelativeToPayload(string payloadDir, string source) =>
        source.Substring(payloadDir.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void Replace(string source, string destination)
    {
        if (IsUpToDate(source, destination))
            return;

        if (Path.GetDirectoryName(destination) is string parent)
            Directory.CreateDirectory(parent);

        string temp = $"{destination}{TempExtension}{Guid.NewGuid():N}";
        File.Copy(source, temp, overwrite: true);
        File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));

#if !NET48
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, File.GetUnixFileMode(source));
#endif

        try
        {
            Move(temp, destination, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MoveRunningCopyAside(destination);
            Move(temp, destination, true);
        }
    }

    private static void Move(string sourceFileName, string destFileName, bool overwrite)
    {
        if (overwrite && System.IO.File.Exists(destFileName))
        {
            System.IO.File.Delete(destFileName);
        }

        System.IO.File.Move(sourceFileName, destFileName);
    }

    // Windows forbids overwriting a running exe but allows renaming one, which live sessions survive.
    private static void MoveRunningCopyAside(string path)
    {
        if (File.Exists(path))
            File.Move(path, $"{path}.{Guid.NewGuid():N}{TrashExtension}");
    }

    private static bool IsUpToDate(string source, string destination)
    {
        FileInfo sourceInfo = new(source);
        FileInfo destinationInfo = new(destination);

        return sourceInfo.Exists
            && destinationInfo.Exists
            && sourceInfo.Length == destinationInfo.Length
            && sourceInfo.LastWriteTimeUtc == destinationInfo.LastWriteTimeUtc;
    }

    private static void SweepLeftovers(string binDir)
    {
        if (!Directory.Exists(binDir))
            return;

        foreach (string leftover in Directory.EnumerateFiles(binDir, $"*{TrashExtension}"))
            DeleteUnlessStillInUse(leftover);

        foreach (string leftover in Directory.EnumerateFiles(binDir, $"*{TempExtension}*"))
            DeleteUnlessStillInUse(leftover);
    }

    private static void DeleteUnlessStillInUse(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Rid
    {
        get
        {
            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{architecture}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{architecture}";
            return $"linux-{architecture}";
        }
    }
}
