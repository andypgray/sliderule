using SolutionDirApp.Core;
using SolutionDirApp.Web;

namespace SolutionDirApp.Plugin;

public sealed class DiscountWidget(Storefront storefront)
{
    public Money Discounted(decimal amount) => storefront.Price(amount * 0.9m);
}
