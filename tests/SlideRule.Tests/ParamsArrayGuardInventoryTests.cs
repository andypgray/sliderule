using System.Linq.Expressions;
using System.Reflection;
using Shouldly;
using SlideRule.Fluent;
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
///     skipping it: an unguarded verb and an unreachable one look identical in a green run otherwise. Add
///     the new type to <see cref="Sample" /> when one appears. A generic verb cannot be invoked open, so the
///     walk closes it over the first of <see cref="SampleTypeArguments" /> its constraints accept, and fails
///     the same way when none does.
/// </remarks>
public sealed class ParamsArrayGuardInventoryTests
{
    private static readonly Arch Arch = new();

    // The type arguments a generic verb is closed over, in the order they are tried; each is one Sample
    // constructs, so the closed verb's receiver and operands are too.
    private static readonly Type[] SampleTypeArguments =
        [typeof(MemberSelection), typeof(MethodSelection), typeof(Selection), typeof(ProjectSelection)];

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
            object? receiver = verb.IsStatic ? null : Sample(verb.DeclaringType!);
            object?[] arguments = parameters
                .Select((parameter, index) => index == parameters.Length - 1 ? null : Sample(parameter.ParameterType))
                .ToArray();

            Exception? thrown = Invoke(verb, receiver, arguments);
            string expected = parameters[parameters.Length - 1].Name!;
            if (thrown is ArgumentNullException guarded && guarded.ParamName == expected) continue;

            string actual = thrown is null
                ? "no exception"
                : $"{thrown.GetType().Name} naming '{(thrown as ArgumentException)?.ParamName}'";
            unguarded.Add($"{verb.DeclaringType!.Name}.{verb.Name}({Signature(parameters)}) -> {actual}");
        }

        unguarded.ShouldBeEmpty();
    }

    private static IEnumerable<MethodInfo> ParamsVerbs()
    {
        return PublicMethods()
            .Where(HasParamsArray)
            .Select(Closed);
    }

    private static MethodInfo Closed(MethodInfo verb)
    {
        if (!verb.IsGenericMethodDefinition) return verb;

        Type[] typeArguments = verb.GetGenericArguments()
            .Select(SampleTypeArgument)
            .ToArray();
        return verb.MakeGenericMethod(typeArguments);
    }

    private static Type SampleTypeArgument(Type typeParameter)
    {
        Type[] constraints = typeParameter.GetGenericParameterConstraints();
        Type? accepted = SampleTypeArguments.FirstOrDefault(candidate =>
            constraints.All(constraint => constraint.IsAssignableFrom(candidate)));

        return accepted ?? throw new NotSupportedException(
            $"No sample type argument for {typeParameter.Name} on {typeParameter.DeclaringMethod!.Name}: a generic "
            + "params verb constrains its type parameter past every type this inventory can construct, so it was "
            + "not exercised. Add one to SampleTypeArguments rather than narrowing the walk.");
    }

    private static IEnumerable<MethodInfo> PublicMethods()
    {
        return typeof(Arch).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.DeclaringType!.Assembly == typeof(Arch).Assembly);
    }

    private static bool HasParamsArray(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
    }

    // Unwraps the reflection layer so the guard's own exception is what the assertion sees.
    private static Exception? Invoke(MethodInfo verb, object? receiver, object?[] arguments)
    {
        try
        {
            verb.Invoke(receiver, arguments);
            return null;
        }
        catch (TargetInvocationException reflected)
        {
            return reflected.InnerException;
        }
        catch (Exception direct)
        {
            return direct;
        }
    }

    private static object Sample(Type parameterType)
    {
        if (parameterType == typeof(string)) return "MyApp.Sample";
        if (parameterType == typeof(Type)) return typeof(ParamsArrayGuardInventoryTests);
        if (parameterType == typeof(Arch)) return Arch;
        if (parameterType == typeof(Layer)) return Arch.Layer(Guid.NewGuid().ToString("N"), "MyApp.*");
        if (parameterType == typeof(Selection)) return Arch.Types;
        if (parameterType == typeof(MemberSelection)) return Arch.Types.Methods;
        if (parameterType == typeof(MethodSelection)) return Arch.Types.Methods;
        if (parameterType == typeof(ProjectSelection)) return Arch.Projects;
        if (parameterType == typeof(Member)) return Arch.Member(typeof(DateTime), nameof(DateTime.Now));
        if (parameterType == typeof(Expression<Action>)) return (Expression<Action>)(() => GC.Collect());
        if (parameterType == typeof(Expression<Func<object?>>)) return (Expression<Func<object?>>)(() => DateTime.Now);
        if (parameterType == typeof(IArchitectureSpec)) return new EmptySpec();
        if (parameterType == typeof(IQuarantinedScope)) return Arch.Scope(ScopeId()).Quarantine(Arch.Types);
        if (parameterType == typeof(ICautionedScope)) return Arch.Scope(ScopeId()).Caution(Arch.Types);

        throw new NotSupportedException(
            $"No sample value for {parameterType.Name}: a params verb takes a receiver or argument this "
            + "inventory cannot construct, so it was not exercised. Add it to Sample rather than narrowing the walk.");
    }

    // Scope IDs must be unique across a model, and these anchors are minted on one shared Arch.
    private static string ScopeId()
    {
        return "inventory/s" + Guid.NewGuid().ToString("N");
    }

    private static string Signature(ParameterInfo[] parameters)
    {
        return string.Join(", ", parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name));
    }

    private sealed class EmptySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
        }
    }
}
