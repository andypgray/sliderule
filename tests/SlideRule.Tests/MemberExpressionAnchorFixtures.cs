namespace SlideRule.Tests;

// Reflectable helper types for the expression-anchor tests and the poison specs in SpecValidationSpecs.cs,
// deliberately spanning the member shapes the resolver classifies. They carry no test-framework
// dependency, so SpecFixtureCorpus can compile SpecValidationSpecs.cs's text with this file beside it and
// bind every anchor without compiling a test class.
internal class AnchorBase
{
    public void Ping()
    {
    }
}

internal sealed class AnchorDerived : AnchorBase;

internal interface IAnchorReadable
{
    void Read();
}

internal sealed class AnchorReadable : IAnchorReadable
{
    public void Read()
    {
    }
}

internal sealed class AnchorWidget
{
    // An ordinary read-write field is one of the shapes the menagerie below exists to offer; readonly
    // would make it a different shape and leave that one unnamed.
    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    public int Size = 1;

    // A menagerie of member shapes for anchor expressions to name; the setters exist to make these
    // ordinary read-write properties, never to be called.
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public int Count { get; set; }

    public AnchorWidget? Inner { get; set; }

    public void Reset()
    {
    }

    // The overload pair exists to be named by a member expression, so its parameters are never read.
#pragma warning disable IDE0060
    public void Over(int value)
    {
    }

    public void Over(string value)
    {
    }
#pragma warning restore IDE0060

    public T Echo<T>(T value)
    {
        return value;
    }
}

internal static class AnchorWidgetExtensions
{
    public static int Doubled(this AnchorWidget widget)
    {
        return widget.Size;
    }
}

internal static class AnchorStatics
{
    public static void Beep()
    {
    }
}

internal readonly struct AnchorCelsius(double value)
{
    public double Value { get; } = value;

    public static explicit operator AnchorFahrenheit(AnchorCelsius c)
    {
        return new AnchorFahrenheit(c.Value * 9 / 5 + 32);
    }
}

internal readonly struct AnchorFahrenheit(double value)
{
    public double Value { get; } = value;
}

// An extension property over int[], beside an ordinary method that shares its accessor's name over an array of
// another rank.
internal static class AnchorShapes
{
    extension(int[] values)
    {
        public int Size => values.Length;
    }

    public static int get_Size(int[,] matrix)
    {
        return matrix.Length;
    }
}

// A generic block, whose emitted accessor is generic over the block's type parameter.
internal static class AnchorGenericShapes
{
    extension<T>(IEnumerable<T> values)
    {
        public int Tally => 0;
    }
}

// A setter-only extension property, whose setter exists to be named, never called.
internal static class AnchorSetters
{
    extension(AnchorWidget widget)
    {
        public int Level
        {
            // ReSharper disable once ValueParameterNotUsed
            set { }
        }
    }
}
