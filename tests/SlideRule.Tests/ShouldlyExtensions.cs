using Shouldly;

namespace SlideRule.Tests;

/// <summary>
///     Case-sensitive string assertions, shadowing the Shouldly originals that default to
///     <see cref="Case.Insensitive" />.
/// </summary>
/// <remarks>
///     <para>
///         Almost every string this suite asserts on is a pinned message, a rendered law sentence, or a
///         symbol ID — text where case <em>is</em> the content. An insensitive default passes a rename that
///         changes only capitalisation. These overloads take fewer parameters than Shouldly's, so a call that
///         names no <see cref="Case" /> binds here; a caller who wants the insensitive comparison asks for it by
///         passing <c>Case.Insensitive</c>, which has no overload here and falls through to Shouldly.
///         Every name has a twin taking <c>customMessage</c>, under Shouldly's own parameter name because
///         call sites pass it by name. A name left without one sends every call that passes a message to
///         Shouldly's insensitive default, and nothing reds.
///     </para>
///     <para>
///         The namespace is essential. Extension lookup walks outward from the innermost enclosing
///         namespace and stops at the first one holding an applicable method, so a class declared in the
///         suite's root namespace is reached before the compilation unit's <c>using Shouldly</c> — from
///         every test file, in every nested namespace, with no edit to any of them.
///         <see cref="Checking.ShouldlyExtensionsTests" /> pins that from a nested namespace.
///     </para>
///     <para>
///         The inner calls are qualified static calls rather than <c>actual.ShouldContain(…)</c>, so an
///         overload added here can never capture them. The extension form reaches Shouldly today only
///         because each call passes a <see cref="Case" />, which no overload here takes; the ReSharper
///         disables stop a full cleanup from rewriting the calls into that form.
///     </para>
///     <para>
///         Collection assertions are untouched — a different receiver type, so nothing here is applicable
///         and lookup falls through.
///     </para>
/// </remarks>
internal static class ShouldlyExtensions
{
    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldContain(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldContain(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldContain(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldContain(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotContain(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldNotContain(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotContain(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldNotContain(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldStartWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldStartWith(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldStartWith(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldStartWith(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldEndWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldEndWith(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldEndWith(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldEndWith(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotStartWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldNotStartWith(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotStartWith(this string actual, string expected, string? customMessage)
    {
        ShouldBeStringTestExtensions.ShouldNotStartWith(actual, expected, Case.Sensitive, customMessage);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotEndWith(this string actual, string expected)
    {
        ShouldBeStringTestExtensions.ShouldNotEndWith(actual, expected, Case.Sensitive);
    }

    // ReSharper disable once InvokeAsExtensionMethod
    // ReSharper disable once InvokeAsExtensionMember
    internal static void ShouldNotEndWith(this string actual, string expected, string? customMessage)
    {
        // Shouldly declares this one overload with the message ahead of the Case, unlike the other five.
        ShouldBeStringTestExtensions.ShouldNotEndWith(actual, expected, customMessage, Case.Sensitive);
    }
}
