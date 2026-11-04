// Stand-ins for two of the framework types the async rules' exemptions name by their full names, so the bed needs
// no web or test framework package to hold a controller action and a test method.

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase;
}

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute;
}
