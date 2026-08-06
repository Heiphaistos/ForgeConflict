using System.Globalization;
using System.Text;
using System.Text.Json;
using ForgeConflict.Core.Collection;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Met un instantane en forme lisible et en enveloppe serialisable. Les sources
/// inexploitables sont toujours nommees : c'est ce qui empeche un inventaire partiel
/// d'etre lu comme un inventaire complet.
/// </summary>
public static class SnapshotReport
{
    public static string Render(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.CurrentCulture, $"Machine  : {snapshot.MachineName}");
        text.AppendLine(CultureInfo.CurrentCulture, $"Systeme  : {snapshot.OsVersion}");
        text.AppendLine(CultureInfo.CurrentCulture, $"Capture  : {snapshot.CapturedAt:g}");
        text.AppendLine();

        if (snapshot.Gpus.IsUsable && snapshot.Gpus.Value is { } gpus)
        {
            text.AppendLine("--- Adaptateurs graphiques ---");
            foreach (var gpu in gpus)
            {
                var generic = gpu.IsGenericMicrosoftDriver ? "  [pilote generique]" : string.Empty;
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {gpu.Name} [{gpu.Vendor}] pilote {gpu.DriverVersion ?? "?"} du {gpu.DriverDate:d} ({gpu.InfName ?? "inf inconnu"}){generic}");
            }
            text.AppendLine();
        }

        if (snapshot.GraphicsSettings.IsUsable && snapshot.GraphicsSettings.Value is { } settings)
        {
            text.AppendLine("--- Reglages graphiques ---");
            foreach (var setting in new[]
                     {
                         settings.TdrLevel, settings.TdrDelay, settings.TdrDdiDelay,
                         settings.HardwareAcceleratedScheduling, settings.MultiPlaneOverlay,
                     })
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"  {setting.Describe()}");
            }
            text.AppendLine();
        }

        if (snapshot.DriverPackages.IsUsable && snapshot.DriverPackages.Value is { } packages)
        {
            text.AppendLine(CultureInfo.CurrentCulture,
                $"--- Magasin de pilotes : {packages.Count} paquet(s) d'affichage ---");
            foreach (var package in packages)
            {
                var signature = package.IsUnsigned ? "NON SIGNE" : "signe";
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {package.PublishedName} <- {package.OriginalName ?? "?"} | {package.ProviderName ?? "?"} | v{package.DriverVersion ?? "?"} | {signature}");
            }
            text.AppendLine();
        }

        if (snapshot.Services.IsUsable && snapshot.Services.Value is { } services)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Services graphiques : {services.Count} ---");
            foreach (var service in services)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {service.Name} [{service.RelatedVendor}] {service.State} / {service.StartMode}");
            }
            text.AppendLine();
        }

        if (snapshot.OverlayProcesses.IsUsable && snapshot.OverlayProcesses.Value is { } overlays)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Overlays actifs : {overlays.Count} ---");
            foreach (var process in overlays)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {process.Name} (PID {process.Id}) {process.Company ?? string.Empty}");
            }
            text.AppendLine();
        }

        if (snapshot.PcieLinks.IsUsable && snapshot.PcieLinks.Value is { } links)
        {
            text.AppendLine("--- Liens PCIe ---");
            foreach (var (adapter, link) in links)
            {
                var flag = link.IsDegraded ? "  [DEGRADE]" : string.Empty;
                text.AppendLine(CultureInfo.CurrentCulture, $"  {adapter} : {link.Describe()}{flag}");
            }
            text.AppendLine();
        }

        if (snapshot.Displays.IsUsable && snapshot.Displays.Value is { } displays)
        {
            text.AppendLine("--- Modes d'affichage ---");
            foreach (var mode in displays)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {mode.AdapterName} : {mode.HorizontalResolution}x{mode.VerticalResolution} a {mode.RefreshRateHz} Hz");
            }
            text.AppendLine();
        }

        if (snapshot.Power.IsUsable && snapshot.Power.Value is { } power)
        {
            text.AppendLine("--- Alimentation ---");
            text.AppendLine(CultureInfo.CurrentCulture,
                $"  Plan actif : {power.ActiveSchemeName ?? "?"} ({power.ActiveSchemeGuid ?? "?"})");
            text.AppendLine(CultureInfo.CurrentCulture, $"  {power.PciExpressLinkStateOnAc.Describe()} (secteur)");
            text.AppendLine(CultureInfo.CurrentCulture, $"  {power.PciExpressLinkStateOnBattery.Describe()} (batterie)");
            text.AppendLine();
        }

        if (snapshot.GpuEvents.IsUsable && snapshot.GpuEvents.Value is { } events)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Evenements GPU : {events.Count} ---");
            foreach (var gpuEvent in events.Take(10))
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {gpuEvent.TimeCreated:g} [{gpuEvent.ProviderName}] id {gpuEvent.EventId} {gpuEvent.Level}");
            }
            if (events.Count == 0) text.AppendLine("  Aucun incident graphique enregistre.");
            text.AppendLine();
        }

        if (snapshot.CrashArtifacts.IsUsable && snapshot.CrashArtifacts.Value is { } crashes)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Traces de plantage : {crashes.Count} ---");
            foreach (var artifact in crashes.Take(10))
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  [{artifact.Kind}] {artifact.Category ?? "-"} {artifact.FileName} ({artifact.SizeBytes / 1024} Ko)");
            }
            if (crashes.Count == 0) text.AppendLine("  Aucun dump de plantage present.");
            text.AppendLine();
        }

        if (snapshot.UnusableSources.Count > 0)
        {
            text.AppendLine("--- Sources indisponibles ---");
            foreach (var fragment in snapshot.UnusableSources)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {fragment.Source} : {fragment.Status} {fragment.Error ?? string.Empty}");
            }
            text.AppendLine();
            text.AppendLine("Le diagnostic est incomplet : les sources ci-dessus n'ont pas repondu.");
        }
        else
        {
            text.AppendLine("Toutes les sources ont repondu.");
        }

        return text.ToString();
    }

    public static SnapshotEnvelope ToEnvelope(SystemSnapshot snapshot, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var records = new List<FragmentRecord>
        {
            Record(snapshot.Gpus),
            Record(snapshot.DriverPackages),
            Record(snapshot.Services),
            Record(snapshot.GraphicsSettings),
            Record(snapshot.OverlayProcesses),
            Record(snapshot.GpuEvents),
            Record(snapshot.CrashArtifacts),
            Record(snapshot.PcieLinks),
            Record(snapshot.Power),
            Record(snapshot.Displays),
        };

        return new SnapshotEnvelope(
            SnapshotEnvelope.CurrentSchemaVersion,
            snapshot.CapturedAt,
            snapshot.MachineName,
            snapshot.OsVersion,
            appVersion,
            records);
    }

    private static FragmentRecord Record<T>(Fragment<T> fragment) =>
        new(fragment.Source,
            fragment.Status,
            fragment.Error,
            fragment.Duration.TotalMilliseconds,
            fragment.IsUsable ? JsonSerializer.Serialize(fragment.Value) : null);
}
