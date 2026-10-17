using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fig.App.ViewModels;

public enum ExportJobStatus
{
    Queued,
    Running,
    Done,
    Failed,
}

/// <summary>One timeline export: output target, status, and live progress.</summary>
public partial class ExportJob : ViewModelBase
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string OutputPath { get; }
    public int Width { get; }
    public int Height { get; }
    public double Fps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private ExportJobStatus _status = ExportJobStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>The encoder the export ended up using ("NVIDIA NVENC", "CPU (x264)"), once it starts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeLabel))]
    private string? _encoderName;

    public ExportJob(string outputPath, int width, int height, double fps)
    {
        OutputPath = outputPath;
        Width = width;
        Height = height;
        Fps = fps;
    }

    public string FileName => Path.GetFileName(OutputPath);
    public string SizeLabel => EncoderName is null ? $"{Width}×{Height} · {Fps:0.###} fps" : $"{Width}×{Height} · {Fps:0.###} fps · {EncoderName}";
    public string StatusLabel => Status switch
    {
        ExportJobStatus.Queued => "Queued",
        ExportJobStatus.Running => $"Exporting \u00b7 {Math.Clamp(Progress, 0, 1) * 100:0}%",
        ExportJobStatus.Done => "Complete",
        ExportJobStatus.Failed => "Failed",
        _ => "",
    };
    public bool IsDone => Status == ExportJobStatus.Done;
    public bool IsActive => Status is ExportJobStatus.Queued or ExportJobStatus.Running;
    public bool HasError => Error is not null;
}
