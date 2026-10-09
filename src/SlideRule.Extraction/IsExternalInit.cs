// netstandard2.0 declares no IsExternalInit, and the compiler requires one to emit an `init` accessor,
// so without this type every positional record in this project would have to give up `init` — which
// means giving up `with`, and hand-writing the equality the record generates. It is internal, so it
// reaches no consumer of the package.
//
// What must stay true: this project grants InternalsVisibleTo to net10 assemblies whose own framework
// also declares IsExternalInit, so those compilations see two. Measured: the compiler's well-known-type
// lookup accepts both, and `init` and `with` go on binding there exactly as before. What it does NOT
// accept is source that names the type explicitly — `typeof(IsExternalInit)` or a using alias to it is
// CS0433, the type existing in two assemblies. No source here or in any friend names it, and none
// should.

// The namespace is the compiler's, not this project's: the type is only found where the compiler
// looks for it.
// ReSharper disable once CheckNamespace

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
