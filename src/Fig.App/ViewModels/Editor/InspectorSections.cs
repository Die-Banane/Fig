using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// One tab of the inspector. Which tabs a selection gets is decided by its facets alone
/// (see <see cref="InspectorSections.For"/>): nothing here asks what kind of thing is selected.
/// Each section's controls are drawn by the data template for its type, bound to <see cref="Host"/>.
/// </summary>
public abstract class InspectorSection : ViewModelBase
{
    protected InspectorSection(PropertiesViewModel host) => Host = host;

    public PropertiesViewModel Host { get; }

    /// <summary>The facet that earns this section a place in the inspector.</summary>
    public abstract Facet Facet { get; }

    /// <summary>The section's name (the tab's tooltip).</summary>
    public abstract string Title { get; }

    public abstract string Icon { get; }
}

public sealed class PictureSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Picture;
    public override string Title => "Picture";
    public override string Icon => "image";
}

public sealed class SoundSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Sound;
    public override string Title => "Audio";
    public override string Icon => "volume-2";
}

public sealed class RetimeSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Retime;
    public override string Title => "Speed";
    public override string Icon => "gauge";
}

public sealed class FadesSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Fades;
    public override string Title => "Fades";
    public override string Icon => "blend";
}

public sealed class EffectsSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Effects;
    public override string Title => "Effects";
    public override string Icon => "sparkles";
}

public sealed class TrackSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Track;
    public override string Title => "Track";
    public override string Icon => "layers";
}

public sealed class MarkerSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Marker;
    public override string Title => "Marker";
    public override string Icon => "bookmark";
}

public sealed class TransitionSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Transition;
    public override string Title => "Transition";
    public override string Icon => "arrow-right-left";
}

public sealed class SourceSection(PropertiesViewModel host) : InspectorSection(host)
{
    public override Facet Facet => Facet.Source;
    public override string Title => "File";
    public override string Icon => "file";
}

/// <summary>The one list of inspector sections: a facet, and the section that edits it, in tab order.</summary>
public static class InspectorSections
{
    private static readonly (Facet Facet, Func<PropertiesViewModel, InspectorSection> Create)[] Registry =
    [
        (Facet.Picture, host => new PictureSection(host)),
        (Facet.Sound, host => new SoundSection(host)),
        (Facet.Retime, host => new RetimeSection(host)),
        (Facet.Fades, host => new FadesSection(host)),
        (Facet.Effects, host => new EffectsSection(host)),
        (Facet.Track, host => new TrackSection(host)),
        (Facet.Marker, host => new MarkerSection(host)),
        (Facet.Transition, host => new TransitionSection(host)),
        (Facet.Source, host => new SourceSection(host)),
    ];

    /// <summary>The facets that have an inspector section so far (a new facet is simply absent until its section exists).</summary>
    public static Facet Supported => Registry.Aggregate(Facet.None, (all, entry) => all | entry.Facet);

    /// <summary>The sections for a selection whose items all have <paramref name="shared"/>, in tab order.</summary>
    public static IEnumerable<InspectorSection> For(Facet shared, PropertiesViewModel host)
        => Registry.Where(entry => (shared & entry.Facet) != 0).Select(entry => entry.Create(host));
}
