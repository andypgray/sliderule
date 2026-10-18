using System.Collections;
using System.Reflection;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Every public method whose last parameter is a sequence, invoked with a null sequence, a sequence
///     holding one null, an empty sequence and a sequence that counts its enumerations. The sequence
///     overloads share one guard, <c>OperandList.ZeroOrMore</c>, so this walks the surface rather than
///     listing them: a new one that builds its list by hand reds here without anyone adding a row.
/// </summary>
/// <remarks>
///     The four contracts are the ones a spec author is told: a null list or a null entry throws at the call,
///     naming the parameter; an empty list is left for the spec's validation to report, so it must not throw
///     here; and the list is read once, so a lazy query a helper returns runs once.
/// </remarks>
public sealed class SequenceGuardInventoryTests
{
    [Fact]
    public void EveryPublicSequenceParameter_HoldsTheFourListContracts()
    {
        List<MethodInfo> methods = SequenceMethods().ToList();

        // A floor, not a pin: the walk reached the seven overloads rather than an empty set.
        methods.Count.ShouldBeGreaterThanOrEqualTo(7);

        var broken = new List<string>();
        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            ParameterInfo sequence = parameters[parameters.Length - 1];
            Type element = sequence.ParameterType.GetGenericArguments()[0];
            var where = $"{method.DeclaringType!.Name}.{method.Name}({SurfaceSamples.Signature(parameters)})";

            string? nullList = NamesTheParameter(Call(method, null), sequence.Name!);
            if (nullList is not null) broken.Add($"{where} given a null list -> {nullList}");

            string? nullEntry = NamesTheParameter(Call(method, ListOf(element, [null])), sequence.Name!);
            if (nullEntry is not null) broken.Add($"{where} given a null entry -> {nullEntry}");

            if (Call(method, ListOf(element, [])) is { } thrownOnEmpty)
                broken.Add($"{where} given an empty list -> {thrownOnEmpty.GetType().Name}, where validation reports it");

            var counting = (ICountedSequence)Activator.CreateInstance(
                typeof(CountingSequence<>).MakeGenericType(element), SurfaceSamples.Sample(element))!;
            Exception? thrownOnOne = Call(method, counting);
            if (thrownOnOne is not null) broken.Add($"{where} given one entry -> {thrownOnOne.GetType().Name}");
            else if (counting.Enumerations != 1) broken.Add($"{where} enumerated its list {counting.Enumerations} times");
        }

        broken.ShouldBeEmpty();
    }

    private static IEnumerable<MethodInfo> SequenceMethods()
    {
        return SurfaceSamples.PublicMethods()
            .Where(EndsInASequence)
            .Select(SurfaceSamples.Closed);
    }

    private static bool EndsInASequence(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0) return false;

        Type last = parameters[parameters.Length - 1].ParameterType;
        return last.IsGenericType && last.GetGenericTypeDefinition() == typeof(IEnumerable<>);
    }

    private static Exception? Call(MethodInfo method, object? sequence)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] arguments = parameters
            .Select((parameter, index) =>
                index == parameters.Length - 1 ? sequence : SurfaceSamples.Sample(parameter.ParameterType))
            .ToArray();

        return SurfaceSamples.Invoke(method, SurfaceSamples.Receiver(method), arguments);
    }

    // Null when the call threw ArgumentNullException naming the parameter, otherwise what it did instead.
    private static string? NamesTheParameter(Exception? thrown, string parameter)
    {
        if (thrown is ArgumentNullException guarded && guarded.ParamName == parameter) return null;

        return thrown is null
            ? "no exception"
            : $"{thrown.GetType().Name} naming '{(thrown as ArgumentException)?.ParamName}'";
    }

    private static IList ListOf(Type element, object?[] entries)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        foreach (object? entry in entries) list.Add(entry);

        return list;
    }

    private interface ICountedSequence
    {
        int Enumerations { get; }
    }

    // One entry, and a count of how many times anything asked for an enumerator.
    private sealed class CountingSequence<T>(T entry) : IEnumerable<T>, ICountedSequence
    {
        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            yield return entry;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
