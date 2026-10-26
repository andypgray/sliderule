using System.Xml.Linq;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Whether a project file is SDK-style, read off the <c>.csproj</c> XML itself — the discriminator that
///     lets <see cref="RestoreFailures" /> blame a missing <c>project.assets.json</c> without blaming the
///     legacy projects that never write one.
/// </summary>
/// <remarks>
///     <para>
///         An SDK-style project can declare its SDK as an attribute on the root
///         (<c>&lt;Project Sdk="Microsoft.NET.Sdk"&gt;</c>, what every template writes), as a child element
///         (<c>&lt;Sdk Name="…" /&gt;</c>), or as a pair of explicit imports
///         (<c>&lt;Import Sdk="…" Project="Sdk.props" /&gt;</c>, the form a project pinning an SDK version
///         between the props and targets halves has to use). All three are the same fact to MSBuild, so all
///         three are the same fact here.
///     </para>
///     <para>
///         Names are matched by local name, never by namespace. The legacy shape declares the 2003 MSBuild XML
///         namespace and the SDK shape declares none, so the <c>xmlns</c> looks like the discriminator and is
///         not one: MSBuild accepts an SDK-style project that declares the 2003 namespace anyway, and the
///         attribute is what decides.
///     </para>
///     <para>
///         The <c>Sdk</c> element is looked for among the root's own children, where MSBuild defines it, rather
///         than anywhere in the document: a legacy project is free to declare a property called <c>Sdk</c>, and
///         reading that as an SDK declaration would blame a healthy legacy project for a file it was never going
///         to write. <c>Import</c> is looked for among all descendants, because an <c>ImportGroup</c> may hold
///         one.
///     </para>
///     <para>
///         Every degradation (an unreadable file, malformed XML, a DOCTYPE, a root element that is not
///         <c>Project</c>, a missing file) answers "not SDK-style", the same posture
///         <see cref="RestoreFailures" /> takes for a file it could not read: a false negative leaves a partial
///         model undetected, where a false positive would refuse a healthy solution.
///     </para>
/// </remarks>
internal static class SdkStyleProject
{
    /// <summary>
    ///     Whether the project file at the absolute <paramref name="projectFilePath" /> declares an MSBuild SDK in
    ///     any of those shapes; false for a legacy project and for every file this cannot read.
    /// </summary>
    internal static bool IsSdkStyle(string projectFilePath)
    {
        if (ProjectFileXml.TryReadProjectRoot(projectFilePath) is not { } root) return false;

        return HasSdkAttribute(root)
               || root.Elements()
                   .Any(element => element.Name.LocalName == "Sdk")
               || root.Descendants()
                   .Any(element => element.Name.LocalName == "Import" && HasSdkAttribute(element));
    }

    // Unprefixed attributes are in no namespace whatever the element's is, so the local name is the whole
    // name here — matched on it anyway, for the same reason the elements are.
    private static bool HasSdkAttribute(XElement element)
    {
        return element.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Sdk");
    }
}
