using System.Reflection;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Every <c>params</c> array on the public surface, invoked with a null array. Almost all of them are
///     covered by one guard — <c>OperandList.OneOrMore</c> checks the array before reading its length, and
///     thirty-odd verbs are built on it — which is exactly why a per-verb test would prove nothing about
///     the verb written next week. This walks the surface instead, so a new verb that builds its operand
///     list by hand reds here without anyone remembering to add a row.
/// </summary>
/// <remarks>
///     The inventory is the point, so it fails on a shape it cannot construct an argument for rather than
///     skipping it (<see cref="SurfaceSamples" />). A generic verb cannot be invoked open, so the walk closes
///     it over the first sample type argument its constraints accept, and fails the same way when none does.
/// </remarks>
public sealed class ParamsArrayGuardInventoryTests
{
    [Fact]
    public void EveryPublicParamsArray_RefusesANullArray_NamingItsOwnParameter()
    {
        List<MethodInfo> verbs = ParamsVerbs().ToList();

        // A floor, not a pin: the claim is that the walk reached the surface rather than an empty set, and
        // a number that only ever grows should not have to be edited by everyone who adds a verb.
        verbs.Count.ShouldBeGreaterThan(35);

        var unguarded = new List<string>();
        foreach (MethodInfo verb in verbs)
        {
            ParameterInfo[] parameters = verb.GetParameters();
            object?[] arguments = parameters
                .Select((parameter, index) =>
                    index == parameters.Length - 1 ? null : SurfaceSamples.Sample(parameter.ParameterType))
                .ToArray();

            Exception? thrown = SurfaceSamples.Invoke(verb, SurfaceSamples.Receiver(verb), arguments);
            string expected = parameters[parameters.Length - 1].Name!;
            if (thrown is ArgumentNullException guarded && guarded.ParamName == expected) continue;

            string actual = thrown is null
                ? "no exception"
                : $"{thrown.GetType().Name} naming '{(thrown as ArgumentException)?.ParamName}'";
            unguarded.Add($"{verb.DeclaringType!.Name}.{verb.Name}({SurfaceSamples.Signature(parameters)}) -> {actual}");
        }

        unguarded.ShouldBeEmpty();
    }

    private static IEnumerable<MethodInfo> ParamsVerbs()
    {
        return SurfaceSamples.PublicMethods()
            .Where(HasParamsArray)
            .Select(SurfaceSamples.Closed);
    }

    private static bool HasParamsArray(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
    }
}
