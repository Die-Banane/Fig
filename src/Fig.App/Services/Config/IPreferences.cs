using System;

namespace Fig.App.Services
{
    public interface IPreferences
    {
        T Get<T>(PreferenceDef def);
        void Set<T>(PreferenceDef def, T value);
        void Reset(PreferenceDef def);
        bool IsModified(PreferenceDef def);
        event Action<PreferenceDef>? Changed;
    }
}
