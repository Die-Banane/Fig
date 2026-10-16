using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>Runs work where the GPU context lives.</summary>
public interface IGpuJobRunner
{
    /// <summary>Runs <paramref name="job"/> with the GPU context (null on a software canvas) and returns its result.</summary>
    Task<T> Run<T>(Func<GRContext?, T> job, TimeSpan? timeout = null);
}

/// <summary>
/// A one-pixel control that gives background work access to the GPU. Avalonia only hands out its Skia GPU
/// context on the render thread, while a draw operation is running, so export queues its frames here and
/// they are rendered during the control's draw: <see cref="Run{T}"/> returns when the job has been executed.
/// Several queued jobs are drained per draw, so throughput is not tied to one frame per screen refresh.
/// </summary>
public sealed class GpuJobHost : Control, IGpuJobRunner
{
    private static readonly TimeSpan DrainBudget = TimeSpan.FromMilliseconds(12);

    private readonly ConcurrentQueue<Action<GRContext?>> _jobs = new();
    private int _redrawQueued;

    public GpuJobHost()
    {
        Width = 1;
        Height = 1;
        IsHitTestVisible = false;
    }

    /// <summary>The host in the visual tree, if any. Set while attached.</summary>
    public static GpuJobHost? Current { get; private set; }

    /// <summary>
    /// True once a job has run on a GPU-backed canvas; false when the canvas turned out to be software.
    /// Null before the first job.
    /// </summary>
    public bool? IsGpuBacked { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Current = this;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Runs <paramref name="job"/> on the render thread with the canvas' GPU context (null on a software
    /// canvas). Faults inside the job fault the returned task. If the control never draws (a minimized
    /// window), the task still completes with a timeout so the caller can fall back.
    /// </summary>
    public Task<T> Run<T>(Func<GRContext?, T> job, TimeSpan? timeout = null)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        _jobs.Enqueue(context =>
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;
            try { completion.TrySetResult(job(context)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        RequestDraw();
        var limit = timeout ?? TimeSpan.FromSeconds(3);
        _ = Task.Delay(limit).ContinueWith(_ =>
        {
            if (Interlocked.Exchange(ref started, 1) == 0)
                completion.TrySetException(new TimeoutException("The GPU did not get a draw in time."));
        });
        return completion.Task;
    }

    private void RequestDraw()
    {
        if (Interlocked.Exchange(ref _redrawQueued, 1) == 0)
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _redrawQueued, 0);
                InvalidateVisual();
            }, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
        => context.Custom(new DrawOperation(this));

    private void Drain(GRContext? context)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < DrainBudget && _jobs.TryDequeue(out var job))
            job(context);
        if (!_jobs.IsEmpty)
            RequestDraw();
    }

    private sealed class DrawOperation : ICustomDrawOperation
    {
        private readonly GpuJobHost _host;

        public DrawOperation(GpuJobHost host) => _host = host;

        public Rect Bounds => new(0, 0, 1, 1);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                _host.IsGpuBacked = false;
                _host.Drain(null);
                return;
            }
            using var lease = feature.Lease();
            _host.IsGpuBacked = lease.GrContext is not null;
            _host.Drain(lease.GrContext);
        }

        public void Dispose()
        {
        }
    }
}
