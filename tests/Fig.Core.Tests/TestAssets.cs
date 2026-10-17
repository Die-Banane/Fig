namespace Fig.Core.Tests;

/// <summary>The media the tests run on, found from wherever the tests run (any checkout, CI included).</summary>
internal static class TestAssets
{
    /// <summary>The repository's <c>tests</c> folder: the first one with an <c>assets</c> folder above the test binary.</summary>
    public static readonly string TestsDir = Find();

    /// <summary>A 3 second WebM (VP9 + Opus) of a counting timer.</summary>
    public static readonly string Timer = Path.Combine(TestsDir, "assets", "3 seconds timer [fxqE27gIZcc].webm");

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var tests = Path.Combine(dir.FullName, "tests");
            if (Directory.Exists(Path.Combine(tests, "assets")))
                return tests;
        }
        throw new DirectoryNotFoundException("tests/assets not found above " + AppContext.BaseDirectory);
    }
}
