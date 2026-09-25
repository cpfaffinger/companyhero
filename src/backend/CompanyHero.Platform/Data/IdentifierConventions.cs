using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CompanyHero.Platform.Data;

/// <summary>Abbildung der Kennungen auf <c>uuid</c>; jeder Modulkontext übernimmt diese Konventionen.</summary>
public static class IdentifierConventions
{
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdConverter>();
        configurationBuilder.Properties<PersonId>().HaveConversion<PersonIdConverter>();
    }

    public sealed class TenantIdConverter() : ValueConverter<TenantId, Guid>(v => v.Value, v => new TenantId(v));

    public sealed class PersonIdConverter() : ValueConverter<PersonId, Guid>(v => v.Value, v => new PersonId(v));
}
