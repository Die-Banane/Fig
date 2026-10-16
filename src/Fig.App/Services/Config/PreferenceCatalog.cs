using System.Collections.Generic;
using System.Linq;

namespace Fig.App.Services
{
    public static class PreferenceCatalog
    {
        public static readonly PreferenceDef SnapReachPx =
            new("editing.snapReachPx", 9.0, Min: 2, Max: 40);

        public static readonly PreferenceDef AudioScrub =
            new("playback.audioScrub", true);

        public static readonly PreferenceDef AutosaveSeconds =
            new("general.autosaveSeconds", 0.8, Min: 0.2, Max: 30);

        public static IReadOnlyList<PreferenceDef> All { get; } =
            [SnapReachPx, AudioScrub, AutosaveSeconds];

        public static PreferenceDef? Find(string key) => All.FirstOrDefault(d => d.Key == key);
    }
}
