using System;
using System.IO;

namespace Fig.App.Services
{
    /// <summary>
    /// The one shared copy of the user's preferences. Read values from <see cref="Current"/>; after changing
    /// any, call <see cref="Save"/>, which also tells listeners through <see cref="Changed"/>.
    /// </summary>
    public sealed class PreferenceService
    {
        private readonly PreferenceLoader? _loader;

        /// <summary>Defaults only, kept in memory and never written (for anything that isn't the running app).</summary>
        public PreferenceService()
        {
            Current = new Preferences();
        }

        public PreferenceService(PreferenceLoader loader)
        {
            _loader = loader;
            Current = loader.Load();
            Current.Normalize();
        }

        public Preferences Current { get; }

        /// <summary>Raised after a save, so anything holding a value can read it again.</summary>
        public event Action? Changed;

        public void Save()
        {
            Current.Normalize();
            try
            {
                _loader?.Save(Current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // preferences are not worth crashing over; the values still apply for this session
            }
            Changed?.Invoke();
        }
    }
}
