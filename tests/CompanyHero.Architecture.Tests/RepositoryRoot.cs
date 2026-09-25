namespace CompanyHero.Architecture.Tests;

internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("Repository-Wurzel (global.json) nicht gefunden.");
    }
}
