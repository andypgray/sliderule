// Compiled under both frameworks, with a framework-conditional region that holds only a using
// directive: under net10.0 that line is disabled text and under netstandard2.0 it is code, so the two
// frameworks read this file differently, but only on a line where neither compilation has a site.
#if !NET
using System.Text;
#endif

namespace MultiTfm.Core
{
}
