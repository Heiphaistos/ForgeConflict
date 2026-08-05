namespace ForgeConflict.Core.Inventory.Model;

/// <summary>Paquet present dans le magasin de pilotes (DriverStore).</summary>
public sealed record DriverPackage(
    string PublishedName,
    string? OriginalName,
    string? ProviderName,
    string? ClassName,
    string? ClassGuid,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? SignerName)
{
    public bool IsDisplayClass =>
        string.Equals(ClassName, "Display", StringComparison.OrdinalIgnoreCase);

    public bool IsUnsigned => string.IsNullOrWhiteSpace(SignerName);
}
