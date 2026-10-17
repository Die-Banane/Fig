using Avalonia;
using System;

namespace Fig.App;

sealed class Program
{
    /// <summary>
    /// Fig's desktop identity: the name of its launcher (packaging/linux/share/applications/io.github.skynse.Fig.desktop)
    /// and of its icon, and its X11 window class, so the taskbar matches the window to the launcher and its icon.
    /// </summary>
    public const string AppId = "io.github.skynse.Fig";

    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new X11PlatformOptions { WmClass = AppId });

        if (OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
        {
            builder = builder.UseWayland();
        }

        return builder;
    }
}
