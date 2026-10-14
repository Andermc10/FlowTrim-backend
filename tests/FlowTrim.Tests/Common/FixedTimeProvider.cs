namespace FlowTrim.Tests.Common;

/// <summary>TimeProvider de prueba que devuelve siempre un instante fijo, para asserts deterministas.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
