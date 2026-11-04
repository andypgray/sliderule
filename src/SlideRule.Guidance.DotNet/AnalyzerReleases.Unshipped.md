; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SR3001 | SlideRule.Guidance | Warning | Do not construct HttpClient outside the composition root
SR3002 | SlideRule.Guidance | Warning | Do not resolve services from IServiceProvider outside the composition root
SR3003 | SlideRule.Guidance | Warning | Do not call BuildServiceProvider
SR3004 | SlideRule.Guidance | Warning | Do not block on a Task or ValueTask
SR3005 | SlideRule.Guidance | Warning | Name a Task-returning method with the Async suffix
SR3006 | SlideRule.Guidance | Warning | Do not catch and swallow Exception outside a top-level handler
SR3007 | SlideRule.Guidance | Disabled | Accept a CancellationToken in a Task-returning method
SR3008 | SlideRule.Guidance | Warning | Do not put persistence mapping attributes on a type
SR3093 | SlideRule.Guidance | Warning | The SlideRule guidance analyzer failed
