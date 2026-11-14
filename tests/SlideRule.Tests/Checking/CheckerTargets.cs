// Reflectable target types for checker specs that use typeof(...). Sources.Hierarchy
// re-declares these plus subject types; the two copies must stay in lockstep — that IS the pin, the
// same discipline as the FullDisplay correspondence test.
// The sub-namespace keeps the targets apart from the tests that select them, and the arity of IHandler<>
// is what the specs bind to — neither follows from the folder or from a use of T.
// ReSharper disable once CheckNamespace

namespace SlideRule.Tests.Checking.Targets;

public interface IThing;

// ReSharper disable once UnusedTypeParameter
public interface IHandler<T>;

public class ThingBase;

public sealed class MarkAttribute : Attribute;

public class Order;

// The reflectable copies of Sources.InheritedAttributes, in the same lockstep.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PinnedAttribute : Attribute;

public sealed class PinnedChildAttribute : PinnedAttribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StampAttribute : Attribute;

// Brand's arity is what the generic-attribute rows bind to, so T has no use.
// ReSharper disable once UnusedTypeParameter
public sealed class BrandAttribute<T> : Attribute;

[Mark]
[Pinned]
[PinnedChild]
[Stamp]
[Stamp]
[Brand<int>]
public class CarryBase;

public class CarryDerived : CarryBase;

public class CarryLeaf : CarryDerived;

[Mark]
public class CarryRedeclared : CarryBase;

public class Bag : List<int>;

[Mark]
public interface IMarked;

public class ViaInterface : IMarked;
