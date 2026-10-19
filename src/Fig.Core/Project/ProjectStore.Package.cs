using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Fig.Core.Media;

namespace Fig.Core.Project
{
    /// <summary>
    /// A project as one file to share: a <c>.figpck</c> is a zip of the project (<c>project.json</c>) and every
    /// file it uses, its media under <c>media/</c> wherever it came from. What Fig makes from media (previews,
    /// proxies) is left out and remade on open; a reversed copy, which is footage, goes in.
    /// </summary>
    public partial class ProjectStore
    {
        public const string PackageExtension = ".figpck";

        /// <summary>
        /// Writes <paramref name="project"/> and its media to <paramref name="packagePath"/>. Media that is missing is
        /// left out (named in the result). <paramref name="progress"/> runs from 0 to 1 by bytes.
        /// </summary>
        public PackResult Pack(Project project, string packagePath, Action<double>? progress = null)
        {
            var result = new PackResult();
            var folder = Path.GetFullPath(ProjectDirectory(project.Id)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // a copy to write: its media named by where it sits in the package
            var copy = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project, Options), Options)!;
            copy.Thumbnail = null;
            var files = new List<(string Source, string Entry)>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bySource = new Dictionary<string, string>();
            foreach (var asset in copy.Media)
            {
                asset.ProxyUrl = null;
                asset.ProxyStatus = ProxyStatus.None;
                asset.Thumbnail = null;
                asset.Filmstrip = null;
                if (string.IsNullOrEmpty(asset.Url) || !File.Exists(asset.Url))
                {
                    if (!string.IsNullOrEmpty(asset.Url))
                        result.Missing.Add(Path.GetFileName(asset.Url));
                    continue;
                }
                var full = Path.GetFullPath(asset.Url);
                // one entry per file, however many media items use it
                if (!bySource.TryGetValue(full, out var entry))
                {
                    entry = full.StartsWith(folder, StringComparison.Ordinal)
                        ? Path.GetRelativePath(folder, full).Replace('\\', '/')
                        : FreeEntry(taken, "media/" + Path.GetFileName(full));
                    taken.Add(entry);
                    bySource[full] = entry;
                    files.Add((full, entry));
                }
                asset.Url = entry;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(packagePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var partial = packagePath + ".partial";
            var total = Math.Max(1L, files.Sum(f => new FileInfo(f.Source).Length));
            long done = 0;
            try
            {
                using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
                {
                    using (var json = zip.CreateEntry("project.json", CompressionLevel.Optimal).Open())
                        JsonSerializer.Serialize(json, copy, Options);
                    foreach (var (source, entry) in files)
                    {
                        // footage is compressed already: stored, not squeezed again
                        using var into = zip.CreateEntry(entry, CompressionLevel.NoCompression).Open();
                        using var from = File.OpenRead(source);
                        var buffer = new byte[1 << 20];
                        int read;
                        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            into.Write(buffer, 0, read);
                            done += read;
                            progress?.Invoke(done / (double)total);
                        }
                        result.Files++;
                    }
                }
                File.Move(partial, packagePath, overwrite: true);
            }
            catch
            {
                try { File.Delete(partial); } catch { /* best effort */ }
                throw;
            }
            return result;
        }

        /// <summary>
        /// Unpacks a package into a new project (the package itself is left as it is) and returns its id, ready to
        /// load. Throws when the file isn't a Fig package.
        /// </summary>
        public string Unpack(string packagePath, Action<double>? progress = null)
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var manifest = zip.GetEntry("project.json") ?? throw new InvalidDataException("Not a Fig project package: it has no project.json");
            Project project;
            using (var json = manifest.Open())
                project = JsonSerializer.Deserialize<Project>(json, Options) ?? throw new InvalidDataException("The package's project.json is empty");

            var id = CreateProject(string.IsNullOrWhiteSpace(project.Name) ? Path.GetFileNameWithoutExtension(packagePath) : project.Name);
            var folder = Path.GetFullPath(ProjectDirectory(id)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var entries = zip.Entries.Where(e => e != manifest && !string.IsNullOrEmpty(e.Name)).ToList();
            var total = Math.Max(1L, entries.Sum(e => e.Length));
            long done = 0;
            foreach (var entry in entries)
            {
                // nothing may land outside the project's folder ("../" in a name)
                var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                if (!target.StartsWith(folder, StringComparison.Ordinal))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                done += entry.Length;
                progress?.Invoke(done / (double)total);
            }

            project.Id = id;
            ProjectPaths.Resolve(project, ProjectDirectory(id));
            SaveProject(project);
            return id;
        }

        private static string FreeEntry(HashSet<string> taken, string entry)
        {
            var name = entry;
            for (var n = 2; taken.Contains(name); n++)
                name = $"media/{Path.GetFileNameWithoutExtension(entry)} ({n}){Path.GetExtension(entry)}";
            return name;
        }
    }

    /// <summary>What <see cref="ProjectStore.Pack"/> did.</summary>
    public class PackResult
    {
        public int Files { get; set; }

        /// <summary>Media the project uses that wasn't where it last saw it, so isn't in the package.</summary>
        public List<string> Missing { get; } = new();
    }
}
