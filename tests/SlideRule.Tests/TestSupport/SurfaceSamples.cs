using System.Linq.Expressions;
using System.Reflection;
using SlideRule.Fluent;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The receivers and arguments the surface inventories invoke the public authoring methods with: a
///     sample value per parameter type, the type arguments a generic method is closed over, and the call
///     that unwraps reflection so the method's own exception is what an assertion sees.
/// </summary>
/// <remarks>
///     An inventory is the point of the tests that use this, so a type it cannot construct a value for
///     fails the walk rather than skipping the method: an unguarded method and an unreachable one look
///     identical in a green run otherwise. Add the new type to <see cref="Sample" /> when one appears.
/// </remarks>
internal static class SurfaceSamples
{
    // The type arguments a generic method is closed over, in the order they are tried; each is one Sample
    // constructs, so the closed method's receiver and operands are too.
    private static readonly Type[] SampleTypeArguments =
        [typeof(MemberSelection), typeof(MethodSelection), typeof(Selection), typeof(ProjectSelection)];

    /// <summary>Every public method the contract assembly declares on an exported type.</summary>
    internal static IEnumerable<MethodInfo> PublicMethods()
    {
        return typeof(Arch).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.DeclaringType!.Assembly == typeof(Arch).Assembly);
    }

    /// <summary>
    ///     The method itself, or a generic one closed over the first of the sample type arguments each of its
    ///     constraints accepts.
    /// </summary>
    internal static MethodInfo Closed(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition) return method;

        Type[] typeArguments = method.GetGenericArguments()
            .Select(SampleTypeArgument)
            .ToArray();
        return method.MakeGenericMethod(typeArguments);
    }

    /// <summary>
    ///     Calls the method with a sample for every parameter but the last, which is <paramref name="last" />,
    ///     returning what it threw with the reflection layer unwrapped, or null.
    /// </summary>
    internal static Exception? CallWithLast(MethodInfo method, object? last)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] arguments = parameters
            .Select((parameter, index) =>
                index == parameters.Length - 1 ? last : Sample(parameter.ParameterType))
            .ToArray();
        object? receiver = Receiver(method);

        return Invoke(method, receiver, arguments);
    }

    /// <summary>
    ///     Null when the call threw ArgumentNullException naming the parameter, otherwise what it did instead.
    /// </summary>
    internal static string? NullGuardMiss(Exception? thrown, string parameter)
    {
        if (thrown is ArgumentNullException guarded && guarded.ParamName == parameter) return null;

        return thrown is null
            ? "no exception"
            : $"{thrown.GetType().Name} naming '{(thrown as ArgumentException)?.ParamName}'";
    }

    /// <summary>The method as a report line names it: its declaring type, its name and its parameter list.</summary>
    internal static string Describe(MethodInfo method)
    {
        string signature = Signature(method.GetParameters());
        return $"{method.DeclaringType!.Name}.{method.Name}({signature})";
    }

    /// <summary>The receiver of an instance method, or null for a static one.</summary>
    private static object? Receiver(MethodInfo method)
    {
        return method.IsStatic ? null : Sample(method.DeclaringType!);
    }

    /// <summary>Calls the method, returning what it threw with the reflection layer unwrapped, or null.</summary>
    private static Exception? Invoke(MethodInfo method, object? receiver, object?[] arguments)
    {
        try
        {
            method.Invoke(receiver, arguments);
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

    /// <summary>A valid value of the parameter type, minted on an Arch of its own.</summary>
    // A fresh Arch per sample, because the inventories that share this run in parallel and an Arch is not safe to
    // register into from two threads. Nothing an inventory calls validates, so operands minted on different
    // instances are never compared.
    internal static object Sample(Type parameterType)
    {
        var arch = new Arch();
        if (parameterType == typeof(string)) return "MyApp.Sample";
        if (parameterType == typeof(Type)) return typeof(SurfaceSamples);
        if (parameterType == typeof(Arch)) return arch;
        if (parameterType == typeof(Layer)) return arch.Layer("Sample", "MyApp.*");
        if (parameterType == typeof(Selection)) return arch.Types;
        if (parameterType == typeof(MemberSelection)) return arch.Types.Methods;
        if (parameterType == typeof(MethodSelection)) return arch.Types.Methods;
        if (parameterType == typeof(ProjectSelection)) return arch.Projects;
        if (parameterType == typeof(Member)) return arch.Member(typeof(DateTime), nameof(DateTime.Now));
        if (parameterType == typeof(Expression<Action>)) return (Expression<Action>)(() => GC.Collect());
        if (parameterType == typeof(Expression<Func<object?>>)) return (Expression<Func<object?>>)(() => DateTime.Now);
        if (parameterType == typeof(IArchitectureSpec)) return new EmptySpec();
        if (parameterType == typeof(IQuarantinedScope)) return arch.Scope("inventory/sample").Quarantine(arch.Types);
        if (parameterType == typeof(ICautionedScope)) return arch.Scope("inventory/sample").Caution(arch.Types);

        throw new NotSupportedException(
            $"No sample value for {parameterType.Name}: a public method takes a receiver or argument this "
            + "inventory cannot construct, so it was not exercised. Add it to Sample rather than narrowing the walk.");
    }

    /// <summary>The parameter list as the report line spells it.</summary>
    private static string Signature(ParameterInfo[] parameters)
    {
        return string.Join(", ", parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name));
    }

    private static Type SampleTypeArgument(Type typeParameter)
    {
        Type[] constraints = typeParameter.GetGenericParameterConstraints();
        Type? accepted = SampleTypeArguments.FirstOrDefault(candidate =>
            constraints.All(constraint => constraint.IsAssignableFrom(candidate)));

        return accepted ?? throw new NotSupportedException(
            $"No sample type argument for {typeParameter.Name} on {typeParameter.DeclaringMethod!.Name}: a generic "
            + "method constrains its type parameter past every type this inventory can construct, so it was not "
            + "exercised. Add one to SampleTypeArguments rather than narrowing the walk.");
    }

    private sealed class EmptySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
        }
    }
}
