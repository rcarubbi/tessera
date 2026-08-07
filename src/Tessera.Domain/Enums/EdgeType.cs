namespace Tessera.Domain.Enums;

public enum EdgeType
{
    Calls,
    References,
    Inherits,
    Implements,
    Imports,
    FieldDependency,
    Publishes,
    Consumes,
    InvokesEndpoint,
    Injected,
    HasMethod
}
