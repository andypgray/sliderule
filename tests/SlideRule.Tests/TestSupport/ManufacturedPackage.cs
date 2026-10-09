using System.IO.Compression;
using System.Text;

namespace SlideRule.Tests.TestSupport;

/// <summary>A NuGet package assembled in memory, one nuspec and one entry, and published to a folder feed.</summary>
internal static class ManufacturedPackage
{
    /// <summary>
    ///     The package's bytes: <c>{id}.nuspec</c> holding <paramref name="nuspec" />, then one entry at
    ///     <paramref name="entryName" /> holding <paramref name="content" />.
    /// </summary>
    internal static byte[] Image(string id, string nuspec, string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var package = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Write(package, $"{id}.nuspec", Encoding.UTF8.GetBytes(nuspec));
            Write(package, entryName, content);
        }

        return buffer.ToArray();
    }

    /// <summary>Writes <paramref name="image" /> into <paramref name="feedDirectory" /> as <c>{id}.{version}.nupkg</c>.</summary>
    internal static void Publish(string feedDirectory, string id, string version, byte[] image)
    {
        Directory.CreateDirectory(feedDirectory);
        File.WriteAllBytes(Path.Combine(feedDirectory, $"{id}.{version}.nupkg"), image);
    }

    private static void Write(ZipArchive package, string entryName, byte[] content)
    {
        using Stream entry = package.CreateEntry(entryName)
            .Open();
        entry.Write(content, 0, content.Length);
    }
}
