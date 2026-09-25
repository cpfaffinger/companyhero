namespace CompanyHero.Api.Contracts;

/// <summary>Release-Stand der laufenden Instanz; der Deploy-Job vergleicht ihn mit dem ausgerollten Digest.</summary>
public sealed record VersionResponse(string Version);
