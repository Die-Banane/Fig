using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.App.Services
{
    /// <summary>
    /// Everything the user can set, as one typed object saved to preferences.json. A property's initial
    /// value is its default; a missing or new property in the file simply keeps that default.
    /// </summary>
    public sealed class Preferences
    {
        public int SchemaVersion { get; set; } = 1;

        public GeneralPreferences General { get; set; } = new();
        public EditingPreferences Editing { get; set; } = new();
        public PlaybackPreferences Playback { get; set; } = new();
        public ExportPreferences Export { get; set; } = new();

        /// <summary>Keys this build doesn't know about (written by a newer one); kept so saving doesn't erase them.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }

        /// <summary>Puts every value back in range after loading, so a hand-edited file can't break the app.</summary>
        public void Normalize()
        {
            General ??= new();
            Editing ??= new();
            Playback ??= new();
            Export ??= new();

            General.AutosaveSeconds = Math.Clamp(General.AutosaveSeconds, 0.2, 30);
            Editing.SnapReachPx = Math.Clamp(Editing.SnapReachPx, 2, 40);
            Editing.UndoSteps = Math.Clamp(Editing.UndoSteps, 10, 1000);
            Export.Width = Math.Clamp(Export.Width, 16, 8192);
            Export.Height = Math.Clamp(Export.Height, 16, 8192);
            Export.Fps = Math.Clamp(Export.Fps, 1, 240);
            Export.Crf = Math.Clamp(Export.Crf, 0, 51);
        }
    }

    public sealed class GeneralPreferences
    {
        public double AutosaveSeconds { get; set; } = 0.8;
        public bool AskBeforeClosingUnsaved { get; set; } = true;
    }

    public sealed class EditingPreferences
    {
        public double SnapReachPx { get; set; } = 9;
        public bool SnapByDefault { get; set; } = true;
        public bool LinkByDefault { get; set; } = true;
        public int UndoSteps { get; set; } = 100;
    }

    public sealed class PlaybackPreferences
    {
        public bool AudioScrub { get; set; } = true;
        public bool Loop { get; set; }
    }

    public sealed class ExportPreferences
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int Fps { get; set; } = 30;
        public int Crf { get; set; } = 23;
        public string? Folder { get; set; }
    }
}
