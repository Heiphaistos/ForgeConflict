using System.Globalization;

namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Etat du lien PCIe d'un adaptateur. Une largeur inferieure au maximum bride la
/// bande passante ; sur portable c'est parfois le cablage, ailleurs le signe d'un
/// slot mal occupe ou d'un reglage d'economie d'energie.
///
/// Un GPU integre au processeur n'est sur aucun lien PCIe et rend <see cref="Unknown"/> :
/// une information absente n'est jamais une degradation.
/// </summary>
public sealed record PcieLink(uint CurrentWidth, uint MaxWidth, uint CurrentSpeedGen, uint MaxSpeedGen)
{
    /// <summary>Lien dont rien n'a pu etre lu : GPU integre, machine virtuelle.</summary>
    public static PcieLink Unknown => new(0, 0, 0, 0);

    public bool IsKnown => CurrentWidth > 0 && MaxWidth > 0;

    public bool IsWidthDegraded => IsKnown && CurrentWidth < MaxWidth;

    public bool IsSpeedDegraded =>
        CurrentSpeedGen > 0 && MaxSpeedGen > 0 && CurrentSpeedGen < MaxSpeedGen;

    public bool IsDegraded => IsWidthDegraded || IsSpeedDegraded;

    public string Describe() => IsKnown
        ? string.Create(CultureInfo.InvariantCulture,
            $"x{CurrentWidth} Gen{CurrentSpeedGen} (maximum x{MaxWidth} Gen{MaxSpeedGen})")
        : "lien PCIe inconnu";
}
