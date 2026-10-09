// Reflectable twins of the two types the drifted-sites checker bed compiles into its Base project, for the
// rows there that name a target with typeof(...). The lockstep is on the NAMES alone — nothing reflects on a
// member here, and a typeof operand resolves against the model by fully-qualified name — so these stay empty.
// Keeping the two spellings of `Base.Registry` and `Base.RegistryException` in step IS the pin, exactly as
// MultiplyDeclaredTargets holds the linked bed's. The exception base is carried anyway, so the twin reads as
// the kind of type the catch and throw rows ban.
// The namespace is the bed's, not the folder's: a typeof twin has to wear the name the extracted node wears.
// ReSharper disable once CheckNamespace

namespace Base;

public class Registry;

public class RegistryException : Exception;
