// The project's global using directives — deliberately one, and the bar for a second is high.
//
// Ct is the running test's cancellation token, and dozens of test classes pass it; the import spares
// each of them a private property of its own. A global using is invisible at the file that depends on
// it, so a reader cannot see where the name comes from. That cost is worth paying only for a name the
// suite already spells one way everywhere it is used, and that none can mistake.

global using static SlideRule.Tests.TestSupport.TestCancellation;
