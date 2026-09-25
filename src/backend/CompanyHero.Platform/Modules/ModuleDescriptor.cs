namespace CompanyHero.Platform.Modules;

/// <summary>
/// Beschreibt ein Fachmodul des Monolithen: Name, Datenbankschema und Stufe in der
/// Abhängigkeitsordnung aus der Domänenkarte, Abschnitt 6. Die Architekturtests
/// leiten aus dieser Ordnung die erlaubten Projektreferenzen ab.
/// </summary>
public sealed record ModuleDescriptor(string Name, string Schema, int DependencyTier);
