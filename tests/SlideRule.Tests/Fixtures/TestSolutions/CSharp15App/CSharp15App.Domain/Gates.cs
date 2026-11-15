namespace CSharp15App.Domain.Gates;

public closed record class GateState;

public record class Shut : GateState;

public record class Open(float Percent) : GateState;

public static class Describer
{
    public static string Describe(GateState state) => state switch
    {
        Shut => "shut",
        Open(var percent) => $"{percent}% open",
    };
}
