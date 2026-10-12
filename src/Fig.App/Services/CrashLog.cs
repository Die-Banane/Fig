using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Fig.App.Services;

/// <summary>
/// Writes unexpected exceptions to the log folder and describes them in words a person can report
/// ("NullReferenceException in PreviewViewModel.ComposeFrame") instead of a bare framework message.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    /// <summary>Logs <paramref name="ex"/> (full trace) and returns "Type in Class.Method".</summary>
    public static string Write(string area, Exception ex)
    {
        var description = Describe(ex);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                File.AppendAllText(Path.Combine(AppPaths.LogDir, "fig.log"), $"{DateTime.Now:O} [{area}] {ex}\n\n");
            }
        }
        catch
        {
            // logging must never make things worse
        }
        return description;
    }

    public static string Describe(Exception ex)
    {
        var method = new StackTrace(ex).GetFrames()
            .Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("Fig.", StringComparison.Ordinal) == true);
        return method is null
            ? ex.GetType().Name
            : $"{ex.GetType().Name} in {method.DeclaringType!.Name}.{method.Name}";
    }
}
