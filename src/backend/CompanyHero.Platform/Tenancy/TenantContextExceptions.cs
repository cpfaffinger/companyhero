namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Fehlender oder ungültiger Kontext führt zu Ablehnung, nie zu unbeschränktem Zugriff (Backend 5.2).
/// Wird geworfen, bevor eine Datenbankoperation ohne Kontext ausgeführt wird.
/// </summary>
public sealed class TenantContextMissingException : InvalidOperationException
{
    public TenantContextMissingException()
        : base("Kein geprüfter Tenant-Kontext für diese Datenbankoperation.")
    {
    }

    public TenantContextMissingException(string message)
        : base(message)
    {
    }

    public TenantContextMissingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Ein Modul hat versucht, Tabellen eines anderen Schemas zu lesen oder zu schreiben (Domänenkarte 1, A-012).
/// </summary>
public sealed class SchemaBoundaryViolationException : InvalidOperationException
{
    public SchemaBoundaryViolationException()
        : base("Zugriff auf ein fremdes Datenbankschema.")
    {
    }

    public SchemaBoundaryViolationException(string message)
        : base(message)
    {
    }

    public SchemaBoundaryViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Eine Entität trägt eine andere <c>tenant_id</c> als der aktive Kontext (Backend 5.1 Nr. 3). Die Datenbank würde
/// den Schreibzugriff über Row Level Security ebenfalls ablehnen; die Anwendung lehnt zuerst und benannt ab.
/// </summary>
public sealed class TenantMismatchException : InvalidOperationException
{
    public TenantMismatchException()
        : base("Die Entität gehört nicht zum aktiven Tenant.")
    {
    }

    public TenantMismatchException(string message)
        : base(message)
    {
    }

    public TenantMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
