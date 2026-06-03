// Polyfill: required for `init` setters and records when targeting netstandard2.0
// (Roslyn analyzers / source generators must target netstandard2.0).
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
