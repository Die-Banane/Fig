using System.Text.Json;
using System.Collections.Generic;

namespace Fig.App.Services {

    public sealed class PreferenceService : IPreferences{
        private readonly PreferenceLoader _loader;
        private readonly Dictionary<string, JsonElement> _values;

        public PreferenceService(PreferenceLoader loader)
        {
            _loader = loader;
            _values = loader.Load();
        }

        private static T Validate<T>(PreferenceDef def, T value) {
            if (value is double number)
            {
                if (def.Min is { } min && number < min) return (T)(object)min;
                if (def.Max is { } max && number > max) return (T)(object)max;

            }

            if (value is string text && def.Choices is { } choices && !choices.Contains(text))
                return (T)def.Default;

            return value;
        }

    }
}
