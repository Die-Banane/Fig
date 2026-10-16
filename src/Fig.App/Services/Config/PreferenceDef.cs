namespace Fig.App.Services
{
    public enum PreferenceScope { User, Project }

    public enum PreferenceApply { Live, NextProject, Restart }

    public sealed record PreferenceDef(
        string Key,
        object Default,
        double? Min = null,
        double? Max = null,
        string[]? Choices = null,
        PreferenceScope Scope = PreferenceScope.User,
        PreferenceApply Apply = PreferenceApply.Live);
}
