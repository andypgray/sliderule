namespace MyApp.Web;

// A request/report endpoint helper that runs its work behind a blanket try/catch and swallows every
// exception — the legacy swallow-and-continue handler pattern. Spelling no `when` filter and returning -1,
// the catch (System.Exception) is red under exceptions/no-general-catch, no-unfiltered-catch and no-swallowed-catch.
// The Invoices field is its only other red: no *Controller name, System.Data, clock read, Task method, Domain ref.
public class ReportEndpoint
{
    public int RenderReport(int reportId)
    {
        try
        {
            return reportId * 2;
        }
        catch (System.Exception)
        {
            return -1;
        }
    }

    // The Reporting half of the web layer's circle: Reporting reaches Invoicing here, and
    // InvoiceCreatedHandler reaches back — the layering/web-cuts-not-circular red.
    public InvoiceService Invoices;
}
