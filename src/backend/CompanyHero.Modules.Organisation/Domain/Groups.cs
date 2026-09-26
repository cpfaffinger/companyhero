using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Organisation.Domain;

/// <summary>Regeln der Gruppen und Sollstärken (Organisation 2, A-034) ohne Infrastruktur.</summary>
public static class GroupRules
{
    /// <summary>Bis zu drei Dimensionen je Tenant (Organisation 2.1).</summary>
    public const int MaxDimensions = 3;

    public const int MaxName = 80;

    /// <summary>Gruppen mit Sollstärke unter fünf erhalten keine Aggregate; die Verwaltung warnt (Organisation 2.2, A-023).</summary>
    public const int HeadcountWarningBelow = 5;

    /// <summary>Voreingestellte Bezeichnungen der drei Dimensionen (Organisation 2.1).</summary>
    public static IReadOnlyList<string> DefaultDimensionNames { get; } = ["Standort", "Abteilung", "Schicht"];

    public static bool WarnsAboutHeadcount(int? headcount) => headcount is { } h && h < HeadcountWarningBelow;
}

/// <summary>Dimension eines Tenants (Organisation 2.1): frei benannt, flache Gruppenliste, umbenennbar und deaktivierbar.</summary>
public sealed class GroupDimension : ITenantOwned
{
    private GroupDimension(TenantId tenantId, Guid id, string name, int position, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        Name = name;
        Position = position;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Name { get; private set; }

    public int Position { get; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; }

    public static GroupDimension Create(TenantId tenantId, string name, int position, DateTimeOffset now)
    {
        if (position is < 0 or >= GroupRules.MaxDimensions)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Höchstens drei Dimensionen (Organisation 2.1).");
        }

        return new GroupDimension(tenantId, Guid.CreateVersion7(), ValidName(name), position, now);
    }

    public void Rename(string name, bool active)
    {
        Name = ValidName(name);
        Active = active;
    }

    internal static string ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > GroupRules.MaxName)
        {
            throw new ArgumentException($"Bezeichnung ist Pflicht und höchstens {GroupRules.MaxName} Zeichen lang.", nameof(name));
        }

        return name.Trim();
    }
}

/// <summary>Gruppe innerhalb einer Dimension (Organisation 2.1): flach, wird archiviert statt gelöscht.</summary>
public sealed class MemberGroup : ITenantOwned
{
    private MemberGroup(TenantId tenantId, Guid id, Guid dimensionId, string name, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        DimensionId = dimensionId;
        Name = name;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid DimensionId { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsSelectable => ArchivedAt is null;

    public static MemberGroup Create(TenantId tenantId, GroupDimension dimension, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        return new MemberGroup(tenantId, Guid.CreateVersion7(), dimension.Id, GroupDimension.ValidName(name), now);
    }

    public void Rename(string name) => Name = GroupDimension.ValidName(name);

    /// <summary>Archivierte Gruppen sind nicht mehr wählbar, bleiben aber für vergangene Auswertungen bestehen (Organisation 2.1).</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;
}

/// <summary>Sollstärke mit Stichtag (Organisation 2.2): je Tenant (Gruppe <c>null</c>) und je Gruppe; die Historie bleibt erhalten.</summary>
public sealed class HeadcountEntry : ITenantOwned
{
    private HeadcountEntry(TenantId tenantId, Guid id, Guid? groupId, DateOnly effectiveFrom, int count, DateTimeOffset recordedAt)
    {
        TenantId = tenantId;
        Id = id;
        GroupId = groupId;
        EffectiveFrom = effectiveFrom;
        Count = count;
        RecordedAt = recordedAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid? GroupId { get; }

    public DateOnly EffectiveFrom { get; }

    public int Count { get; }

    public DateTimeOffset RecordedAt { get; }

    public static HeadcountEntry Record(TenantId tenantId, Guid? groupId, DateOnly effectiveFrom, int count, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new HeadcountEntry(tenantId, Guid.CreateVersion7(), groupId, effectiveFrom, count, now);
    }

    /// <summary>Gültige Sollstärke zu einem Stichtag: der jüngste Eintrag mit Stichtag am oder vor dem Tag.</summary>
    public static int? EffectiveAt(IEnumerable<HeadcountEntry> entries, DateOnly day) =>
        entries.Where(e => e.EffectiveFrom <= day).OrderByDescending(e => e.EffectiveFrom).ThenByDescending(e => e.RecordedAt).Select(e => (int?)e.Count).FirstOrDefault();
}

/// <summary>Zugehörigkeit einer Person zu genau einer Gruppe je Dimension (Organisation 2.1), selbst gewählt, selbst änderbar.</summary>
public sealed class GroupMembership : ITenantOwned
{
    private GroupMembership(TenantId tenantId, PersonId personId, Guid dimensionId, Guid groupId, DateTimeOffset since)
    {
        TenantId = tenantId;
        PersonId = personId;
        DimensionId = dimensionId;
        GroupId = groupId;
        Since = since;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public Guid DimensionId { get; }

    public Guid GroupId { get; private set; }

    public DateTimeOffset Since { get; private set; }

    public static GroupMembership Choose(TenantId tenantId, PersonId personId, MemberGroup group, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!group.IsSelectable)
        {
            throw new OrganisationHierarchyException("Archivierte Gruppen sind nicht wählbar (Organisation 2.1).");
        }

        return new GroupMembership(tenantId, personId, group.DimensionId, group.Id, now);
    }

    /// <summary>Gruppenwechsel: ab jetzt zählt die Person für die neue Gruppe; vergangene Beiträge bleiben bei der alten (Organisation 3.3).</summary>
    public void Change(MemberGroup group, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.DimensionId != DimensionId)
        {
            throw new OrganisationHierarchyException("Die Gruppe gehört zu einer anderen Dimension.");
        }

        if (!group.IsSelectable)
        {
            throw new OrganisationHierarchyException("Archivierte Gruppen sind nicht wählbar (Organisation 2.1).");
        }

        GroupId = group.Id;
        Since = now;
    }
}
