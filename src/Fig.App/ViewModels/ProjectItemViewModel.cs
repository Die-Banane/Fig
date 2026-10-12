using System;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Project;

namespace Fig.App.ViewModels;

/// <summary>A project card/row on the home screen: the stored summary plus display text.</summary>
public sealed class ProjectItemViewModel
{
    public ProjectSummary Summary { get; }

    public ProjectItemViewModel(ProjectSummary summary, HomeViewModel owner)
    {
        Summary = summary;
        OpenCommand = new RelayCommand(() => owner.OpenProjectCommand.Execute(this));
        RenameCommand = new RelayCommand(() => owner.RenameProjectCommand.Execute(this));
        ShowInFolderCommand = new RelayCommand(() => owner.ShowInFolderCommand.Execute(this));
        DeleteCommand = new RelayCommand(() => owner.DeleteProjectCommand.Execute(this));
    }

    public IRelayCommand OpenCommand { get; }
    public IRelayCommand RenameCommand { get; }
    public IRelayCommand ShowInFolderCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    public string Id => Summary.Id;
    public string Name => Summary.Name;
    public double DurationSec => Summary.DurationSec;
    public DateTime UpdatedAt => Summary.UpdatedAt;
    public string? Thumbnail => Summary.Thumbnail;

    public bool HasThumbnail => !string.IsNullOrEmpty(Thumbnail) && System.IO.File.Exists(Thumbnail);
    public string PlaceholderText => string.IsNullOrEmpty(Thumbnail) ? "No preview yet" : "Preview unavailable";

    public string LastEditedText => FormatLastEdited(UpdatedAt, DateTime.Now);

    public string DurationText => FormatDuration(DurationSec);

    public static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
            return "Empty";
        var total = (int)Math.Round(seconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    public static string FormatLastEdited(DateTime when, DateTime now)
    {
        var age = now - when;
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min ago";
        if (when.Date == now.Date)
            return $"Today, {when:h:mm tt}";
        if (when.Date == now.Date.AddDays(-1))
            return "Yesterday";
        return when.Year == now.Year ? when.ToString("MMM d") : when.ToString("MMM d, yyyy");
    }
}
