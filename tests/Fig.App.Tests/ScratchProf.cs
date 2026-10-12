using System.Diagnostics;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Fig.App.ViewModels;
using Fig.Core.Input;
using Fig.Core.Project;

namespace Fig.App.Tests;

public class ScratchProf
{
    [AvaloniaFact]
    public void Profile()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fig", "projects");
        var store = new ProjectStore(root);
        var id = "Benchmark-feature-tour_7164e0e9";
        var project = store.LoadProject(id)!;
        var vm = new EditorViewModel(new GestureRegistry(Path.Combine(Path.GetTempPath(), "g.json")));
        vm.LoadProject(project, store);
        vm.DisposePlayback();
        var preview = vm.Preview;
        var compose = typeof(PreviewViewModel).GetMethod("ComposeFrame", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var width = (int)typeof(PreviewViewModel).GetField("_targetWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        var height = (int)typeof(PreviewViewModel).GetField("_targetHeight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        var pixels = new byte[width * height * 4];
        var lines = new List<string>();
        File.Delete("/tmp/claude-1000/prof.txt");

        // is a single frame at t=0 slow or stuck? run it on a worker with a watchdog
        var probe = Task.Run(() => { var a = new object?[] { 0.0, pixels, false, null }; var sw = Stopwatch.StartNew(); var ok = compose.Invoke(preview, a); File.AppendAllText("/tmp/claude-1000/prof.txt", $"first frame ok={ok} status={a[3]} {sw.ElapsedMilliseconds} ms\n"); });
        if (!probe.Wait(TimeSpan.FromSeconds(25))) { File.AppendAllText("/tmp/claude-1000/prof.txt", "first frame STUCK after 25s\n"); return; }
        double Run(double from, double to, string label)
        {
            var times = new List<double>();
            for (var t = from; t < to; t += 1.0 / 24)
            {
                if (times.Count == 0) File.AppendAllText("/tmp/claude-1000/prof.txt", $"  start {label}\n");
                var args = new object?[] { t, pixels, false, null };
                var sw = Stopwatch.StartNew();
                compose.Invoke(preview, args);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            var avg = times.Average();
            File.AppendAllText("/tmp/claude-1000/prof.txt", $"{label,-34} n={times.Count,3} avg {avg,6:F1} ms  p90 {times[(int)(times.Count * 0.9)],6:F1}  max {times[^1],6:F1}\n");
            lines.Add($"{label,-34} n={times.Count,3} avg {avg,6:F1} ms  p90 {times[(int)(times.Count * 0.9)],6:F1}  max {times[^1],6:F1}");
            return avg;
        }

        // 24 fps steps in playback decode mode; (transition windows: 1 s centred on cuts at 6, 12, 18, 24, 27)
        Run(0.0, 0.5, "warm-up (no transition)");
        Run(2.0, 5.0, "plain cut region 2-5s");
        Run(5.5, 6.5, "cross-dissolve 5.5-6.5s");
        Run(7.0, 11.0, "plain 7-11s");
        Run(11.5, 12.5, "wipe 11.5-12.5s");
        Run(13.0, 17.0, "plain 13-17s");
        Run(17.5, 18.5, "push 17.5-18.5s");
        Run(19.0, 23.0, "plain 19-23s");
        Run(26.5, 27.5, "iris-ish cut ~27s");
        Run(32.5, 33.5, "swirl ~33s");
        File.WriteAllLines("/tmp/claude-1000/prof.txt", lines);
        vm.DisposePlayback();
    }
}
