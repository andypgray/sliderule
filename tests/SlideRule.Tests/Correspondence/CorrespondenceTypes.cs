// Reflectable mirror types for the FullDisplay ⇄ extraction correspondence pin
// (TypeNameFullDisplayTests). Each shape declared here is declared again as source in
// that test and extracted through Roslyn; the two independent renderings must agree. Keep the two
// copies in lockstep — that lockstep IS the pin. The source copy also declares the Uses* carrier types,
// which the test reads only from the extracted model, so they have no mirror here.
// The parameters are never used because arity, not the body, is the shape under test.
// ReSharper disable UnusedTypeParameter

namespace SlideRule.Tests.Correspondence;

public class Simple;

// Outer holds no members of its own because being nested in IS the shape under test, and the source copy
// this is in lockstep with declares it exactly this way. Marking it static would put the two out of step
// over a modifier neither rendering is measuring.
// ReSharper disable once ConvertToStaticClass
public class Outer
{
    public class Inner;
}

public interface IBox<T>;

public class Pair<TFirst, TSecond>;
