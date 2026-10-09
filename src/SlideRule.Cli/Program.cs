using System.CommandLine;
using System.Text;
using SlideRule.Cli;

// Prose fragments carry em-dashes and backticks; force UTF-8 so a real console renders them (the
// in-process tests bypass Console entirely). Guarded: the setter can throw when stdout is redirected.
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Redirected or unsupported handle — leave the console's default encoding in place.
}

// The hook verb reads its payload from stdin. A hand run at a terminal has nothing piped in, and a read there would
// wait on the keyboard, so only a redirected stdin is handed on. Decoded as UTF-8, which is what Claude Code writes,
// whatever code page the console reports.
TextReader? input = Console.IsInputRedirected
    ? new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)
    : null;

return await CliEntry.InvokeAsync(args, new InvocationConfiguration(), input: input);
