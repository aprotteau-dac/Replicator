using System.Text;

namespace Replicator.Core.Scheduling;

public static class PowerShellScheduledTaskLauncher
{
    public static string LauncherPathFor(string scriptPath)
    {
        return Path.ChangeExtension(scriptPath, ".vbs");
    }

    public static string BuildTaskRunCommand(string scriptPath)
    {
        return $"wscript.exe //B //Nologo \"{LauncherPathFor(scriptPath)}\"";
    }

    public static async Task WriteAsync(string scriptPath, CancellationToken cancellationToken = default)
    {
        var launcherPath = LauncherPathFor(scriptPath);
        var directory = Path.GetDirectoryName(launcherPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(launcherPath, BuildLauncherContent(scriptPath), Encoding.UTF8, cancellationToken);
    }

    public static string BuildLauncherContent(string scriptPath)
    {
        return $$"""
            Option Explicit

            Dim shell
            Dim powershellPath
            Dim command
            Dim exitCode

            Set shell = CreateObject("WScript.Shell")
            powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
            command = Quote(powershellPath) & " -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " & Quote({{ToVbStringLiteral(scriptPath)}})
            exitCode = shell.Run(command, 0, True)
            WScript.Quit exitCode

            Function Quote(value)
                Quote = Chr(34) & Replace(value, Chr(34), Chr(34) & Chr(34)) & Chr(34)
            End Function
            """;
    }

    private static string ToVbStringLiteral(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
