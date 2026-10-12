using Fig.App.ViewModels;
using Fig.Core.Project;
using Fig.Core.Timeline;
using TimelineModel=Fig.Core.Timeline.Timeline;
namespace Fig.App.Tests;

public sealed class ExportAndHomeTests : IDisposable
{
    private readonly string _folder=Path.Combine(Path.GetTempPath(),"fig-tests-"+Guid.NewGuid().ToString("N"));
    public ExportAndHomeTests()=>Directory.CreateDirectory(_folder);
    private static TimelineModel Timeline()=>new(){Rate=new FrameRate(30000,1001),Tracks=[new Track{Kind=TrackKind.Video,Clips=[new VideoClip{DurSec=10}]}],Markers=[new Marker{Name="Red",StartSec=1,Color="#FF6B6B"},new Marker{Name="Blue",StartSec=4,Color="#5F9CF8"}]};
    [Fact] public void ColorFilter_KeepsNextDifferentColorBoundary()
    {
        var timeline=Timeline();var vm=new ExportDialogViewModel(timeline.Rate.Fps,1920,1080,timeline){ChapterMode=true,OutputPath=_folder,SelectedColor="#FF6B6B"};
        Assert.Single(vm.Chapters);Assert.True(vm.CanExport);
        var chapter=Assert.Single(vm.CreateOptions().Chapters!);
        Assert.Equal(timeline.Rate.ToSeconds(timeline.Rate.ToFrame(4)),chapter.EndSec);
    }
    [Fact] public void PresetMatchProject_RestoresOriginalDimensions()
    {
        var vm=new ExportDialogViewModel(30,1280,720);vm.SelectedPreset=vm.Presets[1];Assert.Equal(3840,vm.Width);
        vm.SelectedPreset=vm.Presets[0];Assert.Equal(1280,vm.Width);Assert.Equal(720,vm.Height);
    }
    [Fact] public void Chapters_ValidateCollisionsAndRequireASelection()
    {
        var vm=new ExportDialogViewModel(30,1920,1080,Timeline()){ChapterMode=true,OutputPath=_folder};
        Assert.True(vm.CanExport);vm.Chapters[1].FileName=vm.Chapters[0].FileName;Assert.False(vm.CanExport);
        vm.Chapters[1].IsIncluded=false;Assert.True(vm.CanExport);
        File.WriteAllText(Path.Combine(_folder,vm.Chapters[0].FileName),"existing");Assert.False(vm.CanExport);
        File.Delete(Path.Combine(_folder,vm.Chapters[0].FileName));vm.Chapters[0].IsIncluded=false;Assert.False(vm.CanExport);
    }
    [Fact] public void SingleExport_RejectsOddDimensionsAndMissingDestination()
    {
        var vm=new ExportDialogViewModel(30,1920,1080);Assert.False(vm.CanExport);
        vm.OutputPath=Path.Combine(_folder,"export.mp4");Assert.True(vm.CanExport);
        vm.Width=1919;Assert.False(vm.CanExport);
    }
    [Fact] public void Home_SearchAndSort_KeepStoreProjectsIntact()
    {
        var store=new ProjectStore(_folder);store.CreateProject("Robot");store.CreateProject("Product walkthrough");
        var vm=new HomeViewModel(store){SortBy="Name"};Assert.Equal("Product walkthrough",vm.Projects[0].Name);
        vm.SearchText="robot";Assert.Single(vm.Projects);Assert.Equal(2,store.ListProjects().Count);
        vm.SearchText="missing";Assert.True(vm.IsEmpty);
    }
    public void Dispose()=>Directory.Delete(_folder,true);
}
