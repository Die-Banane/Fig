using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Fig.App.ViewModels;
using Fig.Core.Timeline;

namespace Fig.App.Controls;

/// <summary>
/// The one menu for a keyframe, of any kind, wherever its diamond is (the timeline, the inspector's ruler): jump to
/// it, duplicate it to the playhead, choose how it moves on to the next one, delete it. Also the one list of easing
/// names, so every place that offers easing says the same thing.
/// </summary>
public static class KeyframeMenu
{
    /// <summary>The easings' names, in the order of <see cref="KeyframeEasing"/>.</summary>
    public static IReadOnlyList<string> EasingNames { get; } = ["Linear", "Ease in", "Ease out", "Ease in & out", "Hold"];

    public static string NameOf(KeyframeEasing easing) => EasingNames[Math.Clamp((int)easing, 0, EasingNames.Count - 1)];

    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#ff6b6b"));

    /// <summary>The menu for <paramref name="keyframe"/>. <paramref name="changed"/> runs after an edit (to redraw).</summary>
    public static ContextMenu Build(EditorViewModel vm, KeyframeRef keyframe, Action? changed = null)
    {
        var editor = vm.Editor;
        var menu = new ContextMenu();
        var origin = editor.FindKeyframeHost(keyframe.Host)?.OriginSec ?? 0;

        var jump = new MenuItem { Header = "Jump to keyframe" };
        jump.Click += (_, _) => vm.PickKeyframe(keyframe);
        menu.Items.Add(jump);

        var duplicate = new MenuItem { Header = "Duplicate to the playhead" };
        duplicate.Click += (_, _) =>
        {
            var to = vm.PlayheadTimeSec - origin;
            editor.DuplicateKeyframes(keyframe, to);
            if (editor.FindKeyframeHost(keyframe.Host) is { } host)
                vm.PickKeyframe(keyframe with { TimeSec = Math.Clamp(to, 0, host.LimitSec) }, seek: false);
            changed?.Invoke();
        };
        menu.Items.Add(duplicate);

        var current = editor.FindKeyframeHost(keyframe.Host)?.EasingAt(keyframe.TimeSec);
        var easing = new MenuItem { Header = "Movement to the next keyframe" };
        foreach (var option in Enum.GetValues<KeyframeEasing>())
        {
            var chosen = option;
            var item = new MenuItem { Header = (option == current ? "✓  " : "     ") + NameOf(option) };
            item.Click += (_, _) =>
            {
                editor.SetKeyframeEasing(keyframe, chosen);
                changed?.Invoke();
            };
            easing.Items.Add(item);
        }
        menu.Items.Add(easing);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete keyframe", Foreground = Danger };
        delete.Click += (_, _) =>
        {
            vm.DeleteKeyframe(keyframe);
            changed?.Invoke();
        };
        menu.Items.Add(delete);
        return menu;
    }
}
