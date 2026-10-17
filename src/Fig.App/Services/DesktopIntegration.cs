using System.Diagnostics;
using System.Reflection;

namespace Fig.App.Services;

/// <summary>
/// What a release build does for itself, being one file with no installer on Linux: it puts itself in the app menu
/// (a launcher pointing at the file wherever it is, and its icons, for this user only), rewriting the launcher when
/// the file has moved; and it deletes what older versions unpacked. A development build does neither.
/// </summary>
internal static class DesktopIntegration
{
    private const string IconResourcePrefix = "linux-icon:";
    private const string LauncherResource = "linux-launcher";

    /// <summary>A release: one file, which .NET unpacks elsewhere to run, so the app runs from another folder than it.</summary>
    private static bool IsRelease => Environment.ProcessPath is { } exe
        && Path.GetFullPath(Path.GetDirectoryName(exe)!).TrimEnd(Path.DirectorySeparatorChar)
           != Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);

    private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    private static string LauncherPath => Path.Combine(DataHome, "applications", Program.AppId + ".desktop");

    /// <summary>Runs at startup, off the UI thread; nothing here may stop Fig from starting.</summary>
    public static void OnStartup()
    {
        if (!IsRelease)
            return;
        Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsLinux())
                    AddToAppMenu();
                DeleteOldUnpackedVersions();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Desktop integration skipped: {e.Message}");
            }
        });
    }

    /// <summary><c>Fig --uninstall</c>: takes Fig out of the app menu again (the file itself is the user's to delete).</summary>
    public static void RemoveFromAppMenu()
    {
        File.Delete(LauncherPath);
        foreach (var size in Directory.Exists(IconsRoot) ? Directory.GetDirectories(IconsRoot) : [])
            File.Delete(Path.Combine(size, "apps", Program.AppId + ".png"));
        Refresh();
        Console.WriteLine("Removed Fig from the app menu. Delete the Fig file itself to finish.");
    }

    private static string IconsRoot => Path.Combine(DataHome, "icons", "hicolor");

    private static void AddToAppMenu()
    {
        if (Environment.ProcessPath is not { } exe || exe.StartsWith("/tmp/", StringComparison.Ordinal))
            return;   // run from a download folder that won't last: no launcher to a file about to vanish
        var assembly = Assembly.GetExecutingAssembly();
        using var template = new StreamReader(assembly.GetManifestResourceStream(LauncherResource)!);
        var launcher = template.ReadToEnd().Replace("@EXEC@", EscapeExec(exe));
        if (File.Exists(LauncherPath) && File.ReadAllText(LauncherPath) == launcher)
            return;

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(IconResourcePrefix)))
        {
            // the resource is named for its folder under hicolor, as in packaging/linux/share/icons ("256x256/apps/")
            var folder = Path.Combine(IconsRoot, name[IconResourcePrefix.Length..].Replace('\\', '/').Trim('/'));
            Directory.CreateDirectory(folder);
            using var icon = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(Path.Combine(folder, Program.AppId + ".png"));
            icon.CopyTo(file);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LauncherPath)!);
        File.WriteAllText(LauncherPath, launcher);
        Refresh();
    }

    /// <summary>A path as the desktop entry spec wants it inside a quoted Exec argument.</summary>
    private static string EscapeExec(string path)
        => path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");

    /// <summary>Tells the desktop the menu changed (each tool may be missing; the menu catches up on its own).</summary>
    private static void Refresh()
    {
        Run("update-desktop-database", "-q", Path.GetDirectoryName(LauncherPath)!);
        Run("gtk-update-icon-cache", "-q", "-t", IconsRoot);
        Run("kbuildsycoca6");
    }

    private static void Run(string tool, params string[] args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(5000);
        }
        catch
        {
            // not installed: fine
        }
    }

    /// <summary>
    /// .NET unpacks a release into a folder per build (~/.net/Fig/&lt;build&gt; on Linux, %TEMP%\.net\Fig\&lt;build&gt;
    /// on Windows) and never removes it, so every update would leave a few hundred MB behind. This deletes the
    /// other builds' folders beside this one. One still in use by an older Fig that is running fails to delete on
    /// Windows and is left for next time.
    /// </summary>
    private static void DeleteOldUnpackedVersions()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var appFolder = current.Parent;
        // only inside .NET's own unpack folder for this app: never anywhere else
        if (appFolder?.Parent?.Name != ".net" || Environment.ProcessPath is not { } exe
            || !string.Equals(appFolder.Name, Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var other in appFolder.GetDirectories().Where(d => d.FullName != current.FullName))
        {
            try
            {
                other.Delete(recursive: true);
            }
            catch
            {
                // in use
            }
        }
    }
}
