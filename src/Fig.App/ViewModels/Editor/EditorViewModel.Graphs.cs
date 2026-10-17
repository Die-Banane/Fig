using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.App.Views;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Templates;
using Fig.Core.Timeline;
using ProjectModel = Fig.Core.Project.Project;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The user's node-graph effects: creating, editing, duplicating and deleting them.
/// </summary>
public partial class EditorViewModel
{
    /// <summary>The effects the user built from nodes in this project.</summary>
    public GraphEffectLibrary EffectGraphs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGraphEditorOpen))]
    private GraphEditorViewModel? _graphEditor;

    public bool IsGraphEditorOpen => GraphEditor is not null;

    /// <summary>Starts a new effect: an Input wired straight to an Output, ready to build on.</summary>
    [RelayCommand]
    private void NewGraphEffect()
    {
        var graph = new EffectGraph { Name = UniqueGraphName("New effect") };
        var input = new GraphNode { Id = "input", PrimitiveId = PrimitiveCatalog.Input, X = 40, Y = 120 };
        var output = new GraphNode { Id = "output", PrimitiveId = PrimitiveCatalog.Output, X = 520, Y = 120 };
        graph.Nodes.Add(input);
        graph.Nodes.Add(output);
        graph.Edges.Add(new GraphEdge { FromNodeId = "input", FromPort = "image", ToNodeId = "output", ToPort = "image" });
        OpenGraphEditor(graph);
    }

    /// <summary>Opens a saved graph effect (by its catalog type id) in the graph editor.</summary>
    public void EditGraphEffect(string typeId)
    {
        var saved = EffectGraphs.Graphs.FirstOrDefault(g => g.TypeId == typeId);
        if (saved is null)
        {
            Notify?.Invoke("That effect is not part of this project.");
            return;
        }
        OpenGraphEditor(saved.Clone());
    }

    [RelayCommand]
    private void EditGraphEffectFromCatalog(EffectCatalogEntry? entry)
    {
        if (entry is { IsCustom: true })
            EditGraphEffect(entry.TypeId);
    }

    [RelayCommand]
    private void DuplicateGraphEffect(EffectCatalogEntry? entry)
    {
        if (entry is not { IsCustom: true })
            return;
        var id = entry.TypeId[EffectGraph.TypeIdPrefix.Length..];
        if (EffectGraphs.Duplicate(id) is { } copy)
            Notify?.Invoke($"Created \"{copy.Name}\"");
    }

    [RelayCommand]
    private void DeleteGraphEffect(EffectCatalogEntry? entry)
    {
        if (entry is not { IsCustom: true })
            return;
        var id = entry.TypeId[EffectGraph.TypeIdPrefix.Length..];
        var uses = Editor.Document.Tracks.Sum(t => t.Effects.Count(e => e.TypeId == entry.TypeId))
                   + Editor.Document.Tracks.SelectMany(t => t.Clips).Sum(c => c.Effects.Count(e => e.TypeId == entry.TypeId));
        EffectGraphs.Delete(id);
        Preview.RefreshFrame();
        Notify?.Invoke(uses > 0
            ? $"Deleted \"{entry.DisplayName}\". {uses} use{(uses == 1 ? "" : "s")} on the timeline will show the original picture."
            : $"Deleted \"{entry.DisplayName}\"");
    }

    private void OpenGraphEditor(EffectGraph graph)
    {
        var editor = new GraphEditorViewModel(EffectGraphs, graph, message => Notify?.Invoke(message));
        editor.CloseRequested += () =>
        {
            GraphEditor = null;
            Preview.RefreshFrame();
            Properties.Refresh();
        };
        GraphEditor = editor;
    }

    private string UniqueGraphName(string baseName)
    {
        var name = baseName;
        for (var i = 2; EffectGraphs.Graphs.Any(g => g.Name == name); i++)
            name = $"{baseName} {i}";
        return name;
    }

    /// <summary>Called whenever the library changes: refresh the catalog lists, the preview and the autosave.</summary>
    private void OnEffectGraphsChanged()
    {
        OnPropertyChanged(nameof(EffectCatalogItems));
        RefreshEffectFilter();
        Properties?.Refresh();
        Preview?.RefreshFrame();
        IsDirty = true;
        ScheduleAutosave();
    }
}
