namespace System.Runtime.CompilerServices
{
    public interface IUnion
    {
        object? Value { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class IsClosedTypeAttribute : Attribute
    {
        public Type[] DerivedTypes { get; set; } = [];
    }
}
