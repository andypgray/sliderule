// netstandard2.0 declares no IsExternalInit, and the compiler requires one to emit an `init` accessor, so
// without this type no positional record would compile in this project. Internal, so no consumer of the
// package sees it.
//
// This project grants InternalsVisibleTo to net10 assemblies whose framework declares IsExternalInit too, so
// those compilations see two. Measured: the compiler's well-known-type lookup accepts both, and `init` and
// `with` bind as before, but source there that names the type (`typeof(IsExternalInit)`, a using alias) is
// CS0433, so no source here or in a friend may name it.

// The namespace is the compiler's, not this project's: the type is only found where the compiler
// looks for it.
// ReSharper disable once CheckNamespace

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
