namespace MyApp.Domain;

// Applies order rules. Throwing the domain's own OrderRuleViolation is sanctioned (green); the
// throw new System.InvalidOperationException(...) is a BCL throw — the red site for both
// exceptions/domain-throws-domain and exceptions/no-bare-bcl-throw. No Web reference, so the layering rules stay clean.
public class OrderApproval
{
    public void Approve(Order order)
    {
        if (order.Total.Amount < 0)
        {
            throw new OrderRuleViolation("Order total must not be negative.");
        }

        if (!order.Validate())
        {
            throw new System.InvalidOperationException("Order failed validation.");
        }
    }
}
