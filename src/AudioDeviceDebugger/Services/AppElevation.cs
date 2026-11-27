using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace AudioDeviceDebugger.Services;

/// <summary>Whether the app runs with administrator rights, and restarting it with them. The app stays one exe:
/// features that need elevation are greyed out (see <c>Views/AdminOnly</c>) until the user restarts it elevated,
/// instead of running a separate elevated helper.</summary>
public static class AppElevation
{
    /// <summary>True when the process token is elevated. With UAC on, an administrator's normal (filtered) token
    /// reports false, as it should: only a process started "as administrator" has the rights. Checked once; a
    /// process's elevation can't change while it runs.</summary>
    public static bool IsElevated { get; } = CheckElevated();

    /// <summary>The tooltip of a control that needs administrator rights while the app runs without them.</summary>
    public const string AdminRequiredHint =
        "Needs administrator rights. Use \"Restart as admin\" in the toolbar to restart the app with them.";

    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: the user said No to the UAC prompt

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts this same exe again with administrator rights (Windows shows its UAC prompt) and the same
    /// command-line arguments. Says whether the new process started (the caller then quits), the user cancelled the
    /// prompt, or why it failed. Blocks while the UAC prompt is up, so call it off the UI thread.</summary>
    public static RestartResult RestartElevated(IEnumerable<string> args)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return RestartResult.Failed("the path of the running exe is unknown");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = JoinArguments(args),
                UseShellExecute = true, // "runas" only works through ShellExecute
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,
            };
            using var process = Process.Start(startInfo);
            return RestartResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return RestartResult.Cancelled;
        }
        catch (Exception ex)
        {
            return RestartResult.Failed(ex.Message);
        }
    }

    /// <summary>Joins arguments into one command line that Windows (CommandLineToArgvW, and .NET's own parsing)
    /// splits back into the same arguments: an argument with a space, tab or quote (or an empty one) is quoted,
    /// quotes inside it are escaped, and backslashes are doubled only where they come before a quote.</summary>
    internal static string JoinArguments(IEnumerable<string> args)
    {
        var line = new StringBuilder();
        foreach (string arg in args)
        {
            if (line.Length > 0) line.Append(' ');
            AppendArgument(line, arg);
        }
        return line.ToString();
    }

    private static void AppendArgument(StringBuilder line, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            line.Append(arg);
            return;
        }

        line.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                // Backslashes before a quote are doubled, and the quote itself escaped.
                line.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                line.Append('\\', backslashes);
            }
            backslashes = 0;
            line.Append(c);
        }
        // Backslashes before the closing quote are doubled so they don't escape it.
        line.Append('\\', backslashes * 2);
        line.Append('"');
    }
}

/// <summary>The outcome of <see cref="AppElevation.RestartElevated"/>.</summary>
public sealed record RestartResult(bool HasStarted, bool WasCancelled, string? Error)
{
    public static RestartResult Started { get; } = new(true, false, null);
    public static RestartResult Cancelled { get; } = new(false, true, null);
    public static RestartResult Failed(string error) => new(false, false, error);
}
