using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Fig.App.Rendering;
using SkiaSharp;

namespace Fig.App.Views
{
    /// <summary>
    /// The video preview. In GPU mode it draws a <see cref="GpuScene"/> (decoded pictures plus the
    /// effects, crops, transitions and layer blends to apply) straight onto Skia's GPU canvas, so no
    /// picture is composited or copied on the CPU. In CPU mode it shows a finished BGRA frame through
    /// a single reused <see cref="WriteableBitmap"/>. Either way the picture is scaled uniformly to
    /// fit, preserving aspect ratio.
    /// </summary>
    public class PreviewSurface : Control
    {
        private WriteableBitmap? _bitmap;
        private int _bitmapW;
        private int _bitmapH;
        private GpuScene? _scene;
        private bool _backingReported;

        public PreviewSurface()
        {
            ClipToBounds = true;
        }

        /// <summary>
        /// Raised once, on the UI thread, after the first scene is drawn: true when Skia is rendering on
        /// the GPU, false when it falls back to a software canvas (or no Skia lease exists), in which case
        /// the CPU compositor is the faster path.
        /// </summary>
        public event Action<bool>? GpuBackingDetected;

        /// <summary>Shows a scene on the GPU canvas. The surface keeps its own reference to the scene.</summary>
        public void PresentScene(GpuScene scene)
        {
            var previous = _scene;
            _scene = scene.Retain();
            previous?.Dispose();
            InvalidateVisual();
        }

        /// <summary>Writes a BGRA frame into the reused surface and repaints. Allocates a new surface only when the size changes.</summary>
        public void Present(int width, int height, byte[] bgra)
        {
            if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
                return;

            if (_scene is not null)
            {
                _scene.Dispose();
                _scene = null;
            }

            if (_bitmap is null || _bitmapW != width || _bitmapH != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);
                _bitmapW = width;
                _bitmapH = height;
            }

            using (var fb = _bitmap.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(bgra, 0, fb.Address, width * height * 4);
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            if (_scene is { } scene)
            {
                var dest = Fit(scene.Width, scene.Height);
                context.Custom(new SceneDrawOperation(
                    new Rect(0, 0, Bounds.Width, Bounds.Height), scene.Retain(), dest, ReportBacking));
                return;
            }

            if (_bitmap is null)
                return;
            context.DrawImage(_bitmap, Fit(_bitmapW, _bitmapH));
        }

        private Rect Fit(int width, int height)
        {
            var scale = Math.Min(Bounds.Width / width, Bounds.Height / height);
            var w = width * scale;
            var h = height * scale;
            return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        }

        private void ReportBacking(bool gpu)
        {
            if (_backingReported)
                return;
            _backingReported = true;
            Dispatcher.UIThread.Post(() => GpuBackingDetected?.Invoke(gpu));
        }

        /// <summary>Draws the scene on the render thread, using the Skia canvas Avalonia hands us.</summary>
        private sealed class SceneDrawOperation : ICustomDrawOperation
        {
            private readonly GpuScene _scene;
            private readonly Rect _dest;
            private readonly Action<bool> _report;

            public SceneDrawOperation(Rect bounds, GpuScene scene, Rect dest, Action<bool> report)
            {
                Bounds = bounds;
                _scene = scene;
                _dest = dest;
                _report = report;
            }

            public Rect Bounds { get; }

            public bool HitTest(Point p) => false;

            public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature is null)
                {
                    _report(false);
                    return;
                }

                using var lease = feature.Lease();
                var target = new SKRect((float)_dest.Left, (float)_dest.Top, (float)_dest.Right, (float)_dest.Bottom);
                GpuSceneRenderer.Render(lease.SkCanvas, _scene, target);
                _report(lease.GrContext is not null);
            }

            public void Dispose() => _scene.Dispose();
        }
    }
}
