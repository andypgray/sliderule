using SlideRule.Fluent;
using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>Clause and trailer stage for a cautioned scope, which has no boundary or baseline to record.</summary>
internal sealed class CautionedScopeBuilder(ScopeRegistration registration) : ICautionedScope
{
    public ICautionedScope Dragons(string prose)
    {
        registration.Dragons.Add(Guard.NotNull(prose, nameof(prose)));
        return this;
    }

    public ICautionedScope DragonsDoc(string path)
    {
        registration.DragonsDocs.Add(Guard.NotNull(path, nameof(path)));
        return this;
    }

    public ICautionedScope Because(string because)
    {
        registration.Becauses.Add(Guard.NotNull(because, nameof(because)));
        return this;
    }
}
