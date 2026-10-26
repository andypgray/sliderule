using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Reads a project file's XML for the predicates that answer a question from the file itself rather than from
///     its evaluation.
/// </summary>
internal static class ProjectFileXml
{
    /// <summary>
    ///     The root element of the project file at the absolute <paramref name="projectFilePath" />, when that root
    ///     is a <c>Project</c> element matched by local name; null for a missing, unreadable or malformed file, a
    ///     DOCTYPE, and any other root.
    /// </summary>
    /// <remarks>
    ///     A caller treats null as "nothing to report", so a file this cannot read never refuses a healthy solution.
    /// </remarks>
    internal static XElement? TryReadProjectRoot(string projectFilePath)
    {
        try
        {
            // DTD processing is prohibited, so a DOCTYPE raises an XmlException and the file answers null like any
            // other this cannot read. XDocument.Load on a path would parse it and expand its entities.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using FileStream stream = File.OpenRead(projectFilePath);
            using var reader = XmlReader.Create(stream, settings);
            XElement? root = XDocument.Load(reader)
                .Root;

            return root?.Name.LocalName == "Project" ? root : null;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or SecurityException
                                       or ArgumentException
                                       or XmlException)
        {
            return null;
        }
    }
}
