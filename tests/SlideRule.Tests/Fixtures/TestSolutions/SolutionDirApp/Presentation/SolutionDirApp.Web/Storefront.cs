using SolutionDirApp.Core;

namespace SolutionDirApp.Web;

public sealed class Storefront
{
    public Money Price(decimal amount) => new(amount, "EUR");
}
