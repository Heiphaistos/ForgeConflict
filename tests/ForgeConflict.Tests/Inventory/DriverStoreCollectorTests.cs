using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class DriverStoreCollectorTests
{
    /// <summary>
    /// Format reellement produit par pnputil 10.0.26100, capture le 06/08/2026.
    /// Le nom publie est un ATTRIBUT DriverName, pas un element : c'est le piege
    /// qui faisait rendre une liste vide sans qu'aucun test ne le voie.
    /// </summary>
    private const string RealPnpUtilXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <PnpUtil Version="10.0.26100" Command="/enum-drivers /class {4d36e968-e325-11ce-bfc1-08002be10318} /format xml">
            <Driver DriverName="oem17.inf">
                <OriginalName>iigd_dch.inf</OriginalName>
                <ProviderName>Intel Corporation</ProviderName>
                <ClassName>Display</ClassName>
                <ClassGuid>{4d36e968-e325-11ce-bfc1-08002be10318}</ClassGuid>
                <DriverVersion>06/17/2026 32.0.101.7088</DriverVersion>
                <SignerName>Microsoft Windows Hardware Compatibility Publisher</SignerName>
                <CatalogAttributes>
                    <Attribute>Universal</Attribute>
                </CatalogAttributes>
            </Driver>
        </PnpUtil>
        """;

    [Fact]
    public void ParseXml_ReadsThePublishedNameFromTheDriverNameAttribute()
    {
        var package = Assert.Single(DriverStoreCollector.ParseXml(RealPnpUtilXml));

        Assert.Equal("oem17.inf", package.PublishedName);
        Assert.Equal("iigd_dch.inf", package.OriginalName);
        Assert.Equal("Intel Corporation", package.ProviderName);
        Assert.True(package.IsDisplayClass);
        Assert.Equal("32.0.101.7088", package.DriverVersion);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero), package.DriverDate);
        Assert.False(package.IsUnsigned);
    }

    [Fact]
    public void ParseXml_AlsoAcceptsPublishedNameAsAnElement()
    {
        // Repli sur les variantes de format des autres versions de Windows.
        const string xml = """
            <PnpUtil>
              <Driver>
                <PublishedName>oem113.inf</PublishedName>
                <ProviderName>NVIDIA</ProviderName>
                <ClassName>Display</ClassName>
                <DriverVersion>12/30/2025 32.0.15.9174</DriverVersion>
                <SignerName>Microsoft Windows Hardware Compatibility Publisher</SignerName>
              </Driver>
            </PnpUtil>
            """;

        var package = Assert.Single(DriverStoreCollector.ParseXml(xml));
        Assert.Equal("oem113.inf", package.PublishedName);
        Assert.Equal("32.0.15.9174", package.DriverVersion);
    }

    [Fact]
    public void ParseXml_FlagsPackagesWithoutSigner()
    {
        const string xml = """
            <PnpUtil>
              <Driver DriverName="oem9.inf">
                <ClassName>Display</ClassName>
                <DriverVersion>01/02/2020 1.0.0.1</DriverVersion>
              </Driver>
            </PnpUtil>
            """;

        var package = Assert.Single(DriverStoreCollector.ParseXml(xml));
        Assert.True(package.IsUnsigned);
    }

    [Fact]
    public void ParseXml_ReturnsEmptyOnMalformedInputInsteadOfThrowing()
    {
        // Un XML tronque ne doit pas faire tomber le scan entier.
        Assert.Empty(DriverStoreCollector.ParseXml("<PnpUtil><Driver>"));
        Assert.Empty(DriverStoreCollector.ParseXml(""));
    }

    [Fact]
    public void Collect_ReturnsDisplayPackagesWhenTheMachineHasVendorDrivers()
    {
        var packages = new DriverStoreCollector().Collect(CancellationToken.None);

        // Assert.All sur une liste vide passe toujours : c'est ainsi qu'un parseur
        // qui ne trouvait aucun paquet a pu paraitre vert. Le decompte est verifie
        // contre le registre, source independante de pnputil.
        var hasVendorDriver = Microsoft.Win32.Registry.LocalMachine
            .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}")?
            .GetSubKeyNames()
            .Any(n => n.Length == 4 && n.All(char.IsDigit)) ?? false;

        if (hasVendorDriver)
        {
            Assert.NotEmpty(packages);
        }

        Assert.All(packages, p => Assert.False(string.IsNullOrWhiteSpace(p.PublishedName)));
        Assert.All(packages, p => Assert.True(p.IsDisplayClass));
    }
}
