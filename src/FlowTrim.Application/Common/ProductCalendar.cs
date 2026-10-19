namespace FlowTrim.Application.Common;

/// <summary>
/// Calendario del producto: la fecha de referencia ("hoy") se calcula en el servidor en la zona de
/// FlowTrim (America/Bogota). Lo comparten los próximos cobros (HU-04) y la analítica (HU-50).
/// </summary>
public static class ProductCalendar
{
    // Colombia (America/Bogota) no tiene horario de verano, así que un offset fijo UTC-5 es exacto
    // y evita depender de la base de datos de zonas horarias del sistema.
    private static readonly TimeSpan ReferenceOffset = TimeSpan.FromHours(-5);

    /// <summary>Fecha de hoy en la zona del producto según el reloj inyectado.</summary>
    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(ReferenceOffset).DateTime);
}
