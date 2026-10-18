namespace FlowTrim.Tests;

// Prueba temporal que falla a propósito para demostrar que CI bloquea el merge (HU-38). No se fusiona.
public sealed class CiFailureProofTests
{
    [Fact]
    public void CiFailureProof_AlwaysFails() => Assert.Equal(1, 2);
}
