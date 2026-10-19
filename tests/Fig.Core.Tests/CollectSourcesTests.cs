using Fig.Core.Media;
using Fig.Core.Project;
using ProjectModel = Fig.Core.Project.Project;

namespace Fig.Core.Tests;

public class CollectSourcesTests
{
    [Fact]
    public void CollectedProject_KeepsItsMedia_WhenTheFolderIsMovedElsewhere()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fig_collect_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"fig_footage_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outside);
            var footage = Path.Combine(outside, "clip.mp4");
            File.WriteAllText(footage, "pretend video");

            var store = new ProjectStore(root);
            var project = new ProjectModel { Id = store.CreateProject("Trip"), Name = "Trip" };
            project.Media.Add(new MediaAsset { Id = "a", Url = footage });
            project.Media.Add(new MediaAsset { Id = "gone", Url = Path.Combine(outside, "deleted.mp4") });

            var result = new ProjectManager(project, new MediaService(), store.CacheDirectory(project.Id)).CollectSources();
            Assert.Equal(1, result.Copied);
            Assert.Equal(["deleted.mp4"], result.Missing);
            var copied = project.Media[0].Url;
            Assert.Equal(Path.Combine(store.ProjectDirectory(project.Id), "media", "clip.mp4"), copied);
            Assert.True(File.Exists(footage));   // the original is left where it was
            store.SaveProject(project);
            Assert.Equal(copied, project.Media[0].Url);   // saving doesn't change the paths in memory

            // zipped up and unpacked somewhere else, under another name
            var moved = new ProjectStore(Path.Combine(root, "elsewhere"));
            Directory.CreateDirectory(moved.RootDirectory);
            Directory.Move(store.ProjectDirectory(project.Id), moved.ProjectDirectory("trip-copy"));
            var opened = moved.LoadProject("trip-copy")!;
            Assert.Equal(Path.Combine(moved.ProjectDirectory("trip-copy"), "media", "clip.mp4"), opened.Media[0].Url);
            Assert.Equal("pretend video", File.ReadAllText(opened.Media[0].Url));
            Assert.Equal("trip-copy", Assert.Single(moved.ListProjects()).Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}

public class ProjectPackageTests
{
    [Fact]
    public void PackedProject_OpensElsewhere_WithAllItsMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fig_pack_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"fig_footage_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outside);
            var footage = Path.Combine(outside, "clip.mp4");
            File.WriteAllText(footage, "pretend video");

            var store = new ProjectStore(Path.Combine(root, "mine"));
            var project = new ProjectModel { Id = store.CreateProject("Trip"), Name = "Trip" };
            var reversed = Path.Combine(store.CacheDirectory(project.Id), "reversed", "abc.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(reversed)!);
            File.WriteAllText(reversed, "pretend reversed");
            project.Media.Add(new MediaAsset { Id = "a", Url = footage, Thumbnail = "/somewhere/thumb.jpg" });
            project.Media.Add(new MediaAsset { Id = "b", Url = footage });   // the same file twice: packed once
            project.Media.Add(new MediaAsset { Id = "r", Url = reversed, ReverseOf = "a" });
            project.Media.Add(new MediaAsset { Id = "gone", Url = Path.Combine(outside, "deleted.mp4") });

            var package = Path.Combine(root, "Trip" + ProjectStore.PackageExtension);
            var packed = store.Pack(project, package);
            Assert.Equal(2, packed.Files);
            Assert.Equal(["deleted.mp4"], packed.Missing);
            Assert.Equal(footage, project.Media[0].Url);   // packing leaves the open project as it is

            // someone else's Fig
            var theirs = new ProjectStore(Path.Combine(root, "theirs"));
            var opened = theirs.LoadProject(theirs.Unpack(package))!;
            var folder = theirs.ProjectDirectory(opened.Id);
            Assert.Equal("Trip", opened.Name);
            Assert.Equal("pretend video", File.ReadAllText(opened.Media[0].Url));
            Assert.Equal(opened.Media[0].Url, opened.Media[1].Url);
            Assert.Equal("pretend reversed", File.ReadAllText(opened.Media[2].Url));
            Assert.StartsWith(folder, opened.Media[0].Url);
            Assert.Null(opened.Media[0].Thumbnail);   // remade on open, not carried over
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}
