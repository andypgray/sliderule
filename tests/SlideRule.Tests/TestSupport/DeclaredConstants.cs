using System.Reflection;

namespace SlideRule.Tests.TestSupport;

/// <summary>The constants a type declares, read by reflection for the gates that hold a page to them.</summary>
internal static class DeclaredConstants
{
    /// <summary>
    ///     Every constant of type <typeparamref name="T" /> that <paramref name="holder" /> declares, public or not.
    /// </summary>
    internal static IReadOnlyList<T> Of<T>(Type holder)
    {
        return holder.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static field => field is { IsLiteral: true } && field.FieldType == typeof(T))
            .Select(static field => (T)field.GetRawConstantValue()!)
            .ToList();
    }
}
