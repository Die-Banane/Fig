using System;
using System.Threading.Tasks;
using Fig.Core.Media;

namespace Fig.App.Rendering;

/// <summary>
/// Draws export frames with the same shaders the preview uses. Frames are rendered on the GPU through the
/// <see cref="GpuJobHost"/> (or on Skia's raster backend when there is no host) and read back into the
/// exporter's canvas. Several frames are kept in flight so the GPU works while the next frame decodes.
/// </summary>
public sealed class SkiaSceneRenderer : ISceneRenderer
{
    private readonly IGpuJobRunner? _host;
    private readonly CpuSceneRenderer _cpu = new();
    private volatile bool _gpuFailed;

    /// <param name="host">The render-thread host that provides the GPU context, or null to use Skia's raster backend.</param>
    public SkiaSceneRenderer(IGpuJobRunner? host) => _host = host;

    public int MaxInFlight => _host is null ? 1 : 4;

    /// <summary>
    /// The best renderer available now: the GPU when the window's canvas is GPU-backed, otherwise the CPU kernels
    /// (faster than Skia's software shaders). Never throws.
    /// </summary>
    public static async Task<ISceneRenderer> CreateAsync()
    {
        var host = GpuJobHost.Current;
        if (host is null)
            return new CpuSceneRenderer();
        try
        {
            var backed = await host.Run(context => context is not null, TimeSpan.FromSeconds(2));
            return backed ? new SkiaSceneRenderer(host) : new CpuSceneRenderer();
        }
        catch
        {
            return new CpuSceneRenderer();
        }
    }

    public Task RenderAsync(RenderScene scene, byte[] canvas)
    {
        // once the GPU has let us down (the window stopped drawing, the context was lost) the CPU kernels
        // finish the export; waiting out a timeout on every remaining frame would be far slower
        if (_gpuFailed)
            return _cpu.RenderAsync(scene, canvas);
        var gpuScene = GpuSceneBuilder.FromScene(scene);
        if (_host is null)
        {
            try { GpuSceneRenderer.RenderToBgra(gpuScene, null, canvas); }
            finally { gpuScene.Dispose(); }
            return Task.CompletedTask;
        }
        return RenderOnGpu(gpuScene, canvas);
    }

    private async Task RenderOnGpu(GpuScene gpuScene, byte[] canvas)
    {
        try
        {
            await _host!.Run(context =>
            {
                GpuSceneRenderer.RenderToBgra(gpuScene, context, canvas);
                return 0;
            });
        }
        catch (Exception)
        {
            // the window stopped drawing (minimized) or the context was lost: finish this frame in software
            _gpuFailed = true;
            GpuSceneRenderer.RenderToBgra(gpuScene, null, canvas);
        }
        finally
        {
            gpuScene.Dispose();
        }
    }
}
