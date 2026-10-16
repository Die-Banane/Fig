using Avalonia.Input;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.Core.Input;
using Fig.Core.Project;

namespace Fig.App.ViewModels;

public partial class AppViewModel : ViewModelBase
{
    public HomeViewModel Home { get; }
    public EditorViewModel Editor { get; }
    public ToastViewModel Toasts { get; } = new();
    private readonly ProjectStore _store;
    public GestureRegistry Gestures { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private object? _currentView;

    /// <summary>A project is open in the editor (as opposed to the project list or the loading screen).</summary>
    public bool IsEditing => CurrentView is EditorViewModel;

    /// <summary>The user's preferences; one instance shared by everything.</summary>
    public PreferenceService Preferences { get; }

    /// <summary>The Preferences page while it is open, otherwise null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreferencesOpen))]
    private PreferencesViewModel? _preferencesPage;

    public bool IsPreferencesOpen => PreferencesPage is not null;

    [RelayCommand]
    private void OpenPreferences() => PreferencesPage ??= new PreferencesViewModel(Preferences, ClosePreferences,
        IsEditing ? Editor.ProjectManager?.CacheDirectory : null, suspend => ShortcutsSuspended = suspend);

    private void ClosePreferences()
    {
        PreferencesPage = null;
        ShortcutsSuspended = false;
    }

    /// <summary>
    /// True while the Keyboard page is waiting for new keys. Window key bindings run before any key event
    /// reaches the page, so they are taken off the window meanwhile, or pressing Ctrl+S to record it would save.
    /// </summary>
    [ObservableProperty]
    private bool _shortcutsSuspended;

    /// <summary>Each rebindable action's current keys (or null), so the menus show the shortcuts that really apply.</summary>
    public IReadOnlyDictionary<string, KeyGesture?> Shortcuts { get; private set; } = new Dictionary<string, KeyGesture?>();

    private void RefreshShortcuts()
    {
        Shortcuts = ShortcutCatalog.All.ToDictionary(a => a.Id,
            a => ShortcutCatalog.Parse(ShortcutCatalog.GestureFor(Preferences.Current, a)));
        OnPropertyChanged(nameof(Shortcuts));
    }

    public AppViewModel(ProjectStore store, GestureRegistry gestures, PreferenceService? preferences = null)
    {
        _store = store;
        Gestures = gestures;
        Preferences = preferences ?? new PreferenceService();
        RefreshShortcuts();
        Preferences.Changed += RefreshShortcuts;
        Home = new HomeViewModel(store);
        Editor = new EditorViewModel(gestures, Preferences);
        Editor.Notify = msg => Toasts.Show(msg);
        Home.Notify = msg => Toasts.Show(msg);

        Home.ProjectOpened += OpenProject;
        Home.ProjectCreated += OpenProject;

        CurrentView = Home;
    }

    private async void OpenProject(Project project)
    {
        var loading = new LoadingViewModel();
        loading.Update("Opening project...", project.Name);
        CurrentView = loading;

        Editor.LoadProject(project, _store);

        var report = await Editor.ValidateProjectAsync(status =>
        {
            // validate runs on a background thread; marshal progress back to the UI thread
            Dispatcher.UIThread.Post(() => loading.Update(status));
        });

        if (report.HadIssues)
            loading.Update("Project opened", Summary(report));

        CurrentView = Editor;

        // heavy previews (filmstrip / waveform) backfill in the background so open never hangs
        _ = Editor.BackfillMediaPreviewsAsync();

        // the editor needs room to breathe; maximize when we enter it
        var window = MainWindow;
        if (window is not null)
            window.WindowState = WindowState.Maximized;
    }

    private static Window? MainWindow =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mw }
            ? mw
            : null;

    private static string Summary(ProjectValidationReport report)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (report.ArtifactsRepaired > 0)
            parts.Add($"{report.ArtifactsRepaired} preview(s) regenerated");
        if (report.OfflineAssets > 0)
            parts.Add($"{report.OfflineAssets} source(s) offline");
        if (report.FailedArtifacts > 0)
            parts.Add($"{report.FailedArtifacts} preview(s) failed");
        return parts.Count > 0 ? string.Join(", ", parts) : "No issues found";
    }

    public void GoHome()
    {
        Home.Refresh();
        CurrentView = Home;
        var window = MainWindow;
        if (window is not null)
            window.WindowState = WindowState.Normal;
    }

    /// <summary>Closes the open project, returning to the home screen. Prompts to save if there are unsaved changes.</summary>
    public async Task<bool> CloseProjectAsync(Window? owner)
    {
        var editor = CurrentView as EditorViewModel;
        if (editor is not null && editor.IsDirty && !Preferences.Current.General.AskBeforeClosingUnsaved)
            editor.SaveNow();
        else if (editor is not null && editor.IsDirty)
        {
            if (owner is null)
                return false;

            var dialog = new Window
            {
                Title = "Unsaved changes",
                Width = 380,
                Height = 170,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"\"{editor.Project?.Name}\" has unsaved changes. Save before closing?", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Save", Width = 80 };
            var discard = new Button { Content = "Discard", Width = 80 };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            var result = false;
            save.Click += async (_, _) => { editor.SaveNow(); await Task.Delay(50); result = true; dialog.Close(); };
            discard.Click += (_, _) => { result = true; dialog.Close(); };
            cancel.Click += (_, _) => { dialog.Close(); };
            buttons.Children.Add(save);
            buttons.Children.Add(discard);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            await dialog.ShowDialog(owner);

            if (!result)
                return false;
        }

        try
        {
            editor?.DisposePlayback();
        }
        catch
        {
            // teardown failure must not block leaving the project
        }
        GoHome();
        return true;
    }

    [RelayCommand]
    private async Task CloseProject(object? parameter)
    {
        var window = parameter as Window;
        if (window is null && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mw })
            window = mw;
        await CloseProjectAsync(window);
    }
}
