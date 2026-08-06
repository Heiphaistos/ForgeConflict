# ForgeConflict — Lot 2 : interface Fluent 2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer la coque de vérification par une interface Fluent 2 qui présente l'inventaire des dix sources de façon lisible, catégorisée et honnête sur ce qui manque.

**Architecture:** `FluentWindow` de WPF-UI avec matériau Mica et navigation latérale. Trois pages : tableau de bord, inventaire détaillé, journal. Un système de design centralisé (typographie, couleurs d'état) et un contrôle `HealthRing` dessiné en `Path` pour synthétiser l'état des dix sources.

**Tech Stack:** .NET 8, WPF, WPF-UI 4.3.0, CommunityToolkit.Mvvm.

**Version :** 0.3.0 → 0.4.0

---

## Direction esthétique

**Un instrument de mesure, pas une vitrine.** ForgeConflict peut priver la machine d'affichage :
l'interface doit inspirer la rigueur. Densité maîtrisée, données au premier plan, aucune décoration
gratuite.

**Typographie à deux voix.** `Segoe UI Variable` pour le discours, `Cascadia Mono` pour toute
donnée technique — version de pilote, identifiant PCI, largeur de lien. Un outil système qui
affiche `32.0.15.9174` en police proportionnelle perd sa crédibilité, et l'alignement en colonne
rend les écarts de version visibles d'un coup d'œil.

Ces deux polices sont natives de Windows 11 mais **peuvent manquer sur Windows 10** : chaque
déclaration porte donc une chaîne de repli explicite (`Segoe UI`, puis `Consolas`).

**Couleur strictement sémantique.** Aucun accent décoratif. Le vert, l'ambre et le rouge ne servent
qu'à l'état de santé, et rien d'autre n'a le droit de les employer — sinon le signal se dilue.

**Élément signature : l'anneau de santé.** Dix segments, un par source d'inventaire, colorés selon
leur état. Il répond en un regard à la seule question qui compte au démarrage : *est-ce que tout a
répondu ?*

---

### Task 1 : Dépendances et fenêtre Fluent

**Files:**
- Modify: `src/ForgeConflict.App/ForgeConflict.App.csproj`
- Modify: `src/ForgeConflict.App/App.xaml`
- Create: `src/ForgeConflict.App/Theme/Typography.xaml`
- Create: `src/ForgeConflict.App/Theme/Palette.xaml`

- [ ] **Step 1 : Ajouter les paquets**

Modify `src/ForgeConflict.App/ForgeConflict.App.csproj` — ajouter avant `</Project>` :

```xml
  <ItemGroup>
    <PackageReference Include="WPF-UI" Version="4.3.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
  </ItemGroup>
```

- [ ] **Step 2 : Écrire la typographie**

Create `src/ForgeConflict.App/Theme/Typography.xaml` :

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">

    <!-- Deux voix : le discours et la donnee. Les chaines de repli couvrent Windows 10,
         ou Segoe UI Variable et Cascadia Mono peuvent etre absentes. -->
    <FontFamily x:Key="TexteFont">Segoe UI Variable Text, Segoe UI</FontFamily>
    <FontFamily x:Key="TitreFont">Segoe UI Variable Display, Segoe UI</FontFamily>
    <FontFamily x:Key="DonneeFont">Cascadia Mono, Consolas, Courier New</FontFamily>

    <sys:Double x:Key="TailleTitre">28</sys:Double>
    <sys:Double x:Key="TailleSousTitre">18</sys:Double>
    <sys:Double x:Key="TailleTexte">14</sys:Double>
    <sys:Double x:Key="TailleDonnee">13</sys:Double>
    <sys:Double x:Key="TailleLegende">12</sys:Double>

    <Style x:Key="TitrePage" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource TitreFont}" />
        <Setter Property="FontSize" Value="{StaticResource TailleTitre}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Margin" Value="0,0,0,4" />
    </Style>

    <Style x:Key="SousTitre" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource TitreFont}" />
        <Setter Property="FontSize" Value="{StaticResource TailleSousTitre}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>

    <!-- Toute donnee technique passe par ce style : l'alignement en colonne rend les
         ecarts de version visibles immediatement. -->
    <Style x:Key="Donnee" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource DonneeFont}" />
        <Setter Property="FontSize" Value="{StaticResource TailleDonnee}" />
        <Setter Property="TextWrapping" Value="NoWrap" />
    </Style>

    <Style x:Key="Legende" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource TexteFont}" />
        <Setter Property="FontSize" Value="{StaticResource TailleLegende}" />
        <Setter Property="Opacity" Value="0.7" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 3 : Écrire la palette sémantique**

Create `src/ForgeConflict.App/Theme/Palette.xaml` :

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Couleurs strictement semantiques : elles ne servent QU'A l'etat de sante.
         Les employer comme accent decoratif diluerait le signal. -->
    <Color x:Key="CouleurSainC">#2E9E5B</Color>
    <Color x:Key="CouleurAttentionC">#C98A18</Color>
    <Color x:Key="CouleurCritiqueC">#C4432B</Color>
    <Color x:Key="CouleurInconnuC">#7A7A7A</Color>

    <SolidColorBrush x:Key="Sain" Color="{StaticResource CouleurSainC}" />
    <SolidColorBrush x:Key="Attention" Color="{StaticResource CouleurAttentionC}" />
    <SolidColorBrush x:Key="Critique" Color="{StaticResource CouleurCritiqueC}" />
    <SolidColorBrush x:Key="Inconnu" Color="{StaticResource CouleurInconnuC}" />

    <SolidColorBrush x:Key="SainDoux" Color="{StaticResource CouleurSainC}" Opacity="0.14" />
    <SolidColorBrush x:Key="AttentionDoux" Color="{StaticResource CouleurAttentionC}" Opacity="0.14" />
    <SolidColorBrush x:Key="CritiqueDoux" Color="{StaticResource CouleurCritiqueC}" Opacity="0.14" />
</ResourceDictionary>
```

- [ ] **Step 4 : Fusionner les dictionnaires**

Replace the whole content of `src/ForgeConflict.App/App.xaml` :

```xml
<Application x:Class="ForgeConflict.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
                <ResourceDictionary Source="Theme/Typography.xaml" />
                <ResourceDictionary Source="Theme/Palette.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 5 : Compiler**

Run : `dotnet build ForgeConflict.sln --nologo`
Expected : `0 Erreur(s)`.

- [ ] **Step 6 : Commit**

```bash
git add -A
git commit -m "Add Fluent 2 theming with a two-voice type scale and semantic palette"
```

---

### Task 2 : Modèle de vue de l'état de santé

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/SourceHealth.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/SourceHealthTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/SourceHealthTests.cs` :

```csharp
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class SourceHealthTests
{
    private static Fragment<int> Fragment(FragmentStatus status) =>
        new("source", status, 1, status == FragmentStatus.Failed ? "erreur" : null, TimeSpan.FromMilliseconds(5));

    [Fact]
    public void FromFragment_MapsEachStatusToItsOwnLevel()
    {
        Assert.Equal(HealthLevel.Sain, SourceHealth.FromFragment(Fragment(FragmentStatus.Ok)).Level);
        Assert.Equal(HealthLevel.Critique, SourceHealth.FromFragment(Fragment(FragmentStatus.Failed)).Level);
        Assert.Equal(HealthLevel.Attention, SourceHealth.FromFragment(Fragment(FragmentStatus.TimedOut)).Level);
    }

    [Fact]
    public void FromFragment_ExplainsATimeoutDifferentlyFromAFailure()
    {
        var timedOut = SourceHealth.FromFragment(Fragment(FragmentStatus.TimedOut));
        var failed = SourceHealth.FromFragment(Fragment(FragmentStatus.Failed));

        Assert.Contains("delai", timedOut.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(timedOut.Explanation, failed.Explanation);
    }

    [Fact]
    public void Overall_IsCriticalAsSoonAsOneSourceFailed()
    {
        var levels = new[] { HealthLevel.Sain, HealthLevel.Sain, HealthLevel.Critique };

        Assert.Equal(HealthLevel.Critique, SourceHealth.Overall(levels));
    }

    [Fact]
    public void Overall_IsHealthyOnlyWhenEverySourceAnswered()
    {
        Assert.Equal(HealthLevel.Sain, SourceHealth.Overall([HealthLevel.Sain, HealthLevel.Sain]));
        Assert.Equal(HealthLevel.Attention, SourceHealth.Overall([HealthLevel.Sain, HealthLevel.Attention]));
    }

    [Fact]
    public void Overall_OfNothingIsUnknownRatherThanHealthy()
    {
        // Aucune source evaluee ne signifie pas que tout va bien : le dire serait
        // presenter une absence de mesure comme un bon resultat.
        Assert.Equal(HealthLevel.Inconnu, SourceHealth.Overall([]));
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet vstest tests\ForgeConflict.Tests\bin\x64\Debug\net8.0-windows\ForgeConflict.Tests.dll`
Expected : échec de compilation, `SourceHealth` étant introuvable.

- [ ] **Step 3 : Écrire le modèle**

Create `src/ForgeConflict.Core/Inventory/SourceHealth.cs` :

```csharp
using ForgeConflict.Core.Collection;

namespace ForgeConflict.Core.Inventory;

public enum HealthLevel
{
    Inconnu,
    Sain,
    Attention,
    Critique,
}

/// <summary>
/// Traduit l'etat d'une source en niveau de sante lisible, avec l'explication
/// correspondante. Un delai depasse et un echec ne sont pas equivalents : le premier
/// laisse esperer une reponse, le second signale une source inaccessible.
/// </summary>
public sealed record SourceHealth(string Source, HealthLevel Level, string Explanation, TimeSpan Duration)
{
    public static SourceHealth FromFragment(IFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var (level, explanation) = fragment.Status switch
        {
            FragmentStatus.Ok => (HealthLevel.Sain, "Source interrogee avec succes."),
            FragmentStatus.TimedOut => (HealthLevel.Attention,
                "La source n'a pas repondu dans le delai imparti. Le diagnostic est incomplet."),
            _ => (HealthLevel.Critique,
                $"La source est inaccessible : {fragment.Error ?? "cause inconnue"}."),
        };

        return new SourceHealth(fragment.Source, level, explanation, fragment.Duration);
    }

    /// <summary>
    /// Niveau global : le pire des niveaux observes. Une liste vide rend
    /// <see cref="HealthLevel.Inconnu"/> et non <see cref="HealthLevel.Sain"/> :
    /// une absence de mesure n'est pas un bon resultat.
    /// </summary>
    public static HealthLevel Overall(IReadOnlyCollection<HealthLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0) return HealthLevel.Inconnu;

        if (levels.Contains(HealthLevel.Critique)) return HealthLevel.Critique;
        if (levels.Contains(HealthLevel.Attention)) return HealthLevel.Attention;
        if (levels.Contains(HealthLevel.Inconnu)) return HealthLevel.Inconnu;

        return HealthLevel.Sain;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet build ForgeConflict.sln --nologo; dotnet vstest tests\ForgeConflict.Tests\bin\x64\Debug\net8.0-windows\ForgeConflict.Tests.dll`
Expected : les 5 tests `SourceHealthTests` passent.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory tests/ForgeConflict.Tests/Inventory
git commit -m "Add source health model separating timeout from failure"
```

---

### Task 3 : Anneau de santé

**Files:**
- Create: `src/ForgeConflict.App/Controls/HealthRing.cs`

- [ ] **Step 1 : Écrire le contrôle**

Create `src/ForgeConflict.App/Controls/HealthRing.cs` :

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.App.Controls;

/// <summary>
/// Anneau segmente : un arc par source d'inventaire, colore selon son etat.
/// Repond d'un regard a la question qui compte au demarrage : tout a-t-il repondu ?
/// </summary>
public sealed class HealthRing : Control
{
    private const double Thickness = 14;
    private const double GapDegrees = 3;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<HealthLevel>), typeof(HealthRing),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HealthLevel>? Segments
    {
        get => (IReadOnlyList<HealthLevel>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    static HealthRing()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HealthRing), new FrameworkPropertyMetadata(typeof(HealthRing)));
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var segments = Segments;
        if (segments is null || segments.Count == 0) return;

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        var sweep = 360.0 / segments.Count;

        for (var i = 0; i < segments.Count; i++)
        {
            // -90 place le premier segment en haut plutot qu'a droite.
            var start = (i * sweep) - 90 + (GapDegrees / 2);
            var end = start + sweep - GapDegrees;

            var pen = new Pen(BrushFor(segments[i]), Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };

            context.DrawGeometry(null, pen, BuildArc(center, radius, start, end));
        }
    }

    private static Geometry BuildArc(Point center, double radius, double startDegrees, double endDegrees)
    {
        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, endDegrees);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            point: end,
            size: new Size(radius, radius),
            rotationAngle: 0,
            isLargeArc: (endDegrees - startDegrees) > 180,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }

    private Brush BrushFor(HealthLevel level)
    {
        var key = level switch
        {
            HealthLevel.Sain => "Sain",
            HealthLevel.Attention => "Attention",
            HealthLevel.Critique => "Critique",
            _ => "Inconnu",
        };

        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
```

- [ ] **Step 2 : Compiler**

Run : `dotnet build ForgeConflict.sln --nologo`
Expected : `0 Erreur(s)`.

- [ ] **Step 3 : Commit**

```bash
git add src/ForgeConflict.App/Controls
git commit -m "Add segmented health ring control, one arc per inventory source"
```

---

### Task 4 : Fenêtre, navigation et pages

**Files:**
- Create: `src/ForgeConflict.App/MainWindow.xaml` (remplacement complet)
- Create: `src/ForgeConflict.App/MainWindow.xaml.cs` (remplacement complet)
- Create: `src/ForgeConflict.App/Views/DashboardPage.xaml` + `.cs`
- Create: `src/ForgeConflict.App/Views/InventoryPage.xaml` + `.cs`
- Create: `src/ForgeConflict.App/Views/LogPage.xaml` + `.cs`
- Create: `src/ForgeConflict.App/ViewModels/ShellViewModel.cs`

- [ ] **Step 1 : Écrire le modèle de vue partagé**

Create `src/ForgeConflict.App/ViewModels/ShellViewModel.cs` :

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App.ViewModels;

/// <summary>
/// Etat partage par les pages : un seul scan alimente le tableau de bord,
/// l'inventaire et le journal.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ILog _log;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private SystemSnapshot? _snapshot;

    [ObservableProperty]
    private HealthLevel _overall = HealthLevel.Inconnu;

    [ObservableProperty]
    private string _report = "Aucune analyse effectuee.";

    public ObservableCollection<SourceHealth> Sources { get; } = [];

    public ShellViewModel(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        try
        {
            var scanner = new InventoryScanner(new IsolatedRunner(_log));
            var snapshot = await scanner.ScanAsync().ConfigureAwait(true);

            Snapshot = snapshot;
            Report = SnapshotReport.Render(snapshot);

            Sources.Clear();
            foreach (var fragment in snapshot.AllFragments)
            {
                Sources.Add(SourceHealth.FromFragment(fragment));
            }

            Overall = SourceHealth.Overall(Sources.Select(s => s.Level).ToList());
            _log.Write(LogLevel.Info,
                $"analyse terminee, etat global {Overall}, {snapshot.UnusableSources.Count} source(s) indisponible(s)");
        }
        finally
        {
            IsScanning = false;
        }
    }
}
```

- [ ] **Step 2 : Écrire la fenêtre**

Create `src/ForgeConflict.App/MainWindow.xaml` :

```xml
<ui:FluentWindow x:Class="ForgeConflict.App.MainWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 xmlns:views="clr-namespace:ForgeConflict.App.Views"
                 Title="ForgeConflict" Height="760" Width="1180"
                 MinHeight="560" MinWidth="900"
                 WindowStartupLocation="CenterScreen"
                 WindowBackdropType="Mica"
                 ExtendsContentIntoTitleBar="True">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="ForgeConflict" />

        <ui:NavigationView x:Name="Navigation" Grid.Row="1"
                           PaneDisplayMode="Left" OpenPaneLength="220"
                           IsBackButtonVisible="Collapsed">
            <ui:NavigationView.MenuItems>
                <ui:NavigationViewItem Content="Tableau de bord"
                                       TargetPageType="{x:Type views:DashboardPage}">
                    <ui:NavigationViewItem.Icon>
                        <ui:SymbolIcon Symbol="Gauge24" />
                    </ui:NavigationViewItem.Icon>
                </ui:NavigationViewItem>
                <ui:NavigationViewItem Content="Inventaire"
                                       TargetPageType="{x:Type views:InventoryPage}">
                    <ui:NavigationViewItem.Icon>
                        <ui:SymbolIcon Symbol="Board24" />
                    </ui:NavigationViewItem.Icon>
                </ui:NavigationViewItem>
                <ui:NavigationViewItem Content="Journal"
                                       TargetPageType="{x:Type views:LogPage}">
                    <ui:NavigationViewItem.Icon>
                        <ui:SymbolIcon Symbol="DocumentText24" />
                    </ui:NavigationViewItem.Icon>
                </ui:NavigationViewItem>
            </ui:NavigationView.MenuItems>
        </ui:NavigationView>
    </Grid>
</ui:FluentWindow>
```

Create `src/ForgeConflict.App/MainWindow.xaml.cs` :

```csharp
using System.Windows;
using ForgeConflict.App.ViewModels;
using ForgeConflict.App.Views;
using Wpf.Ui.Controls;

namespace ForgeConflict.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    public ShellViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        Navigation.Navigate(typeof(DashboardPage));
}
```

- [ ] **Step 3 : Écrire le tableau de bord**

Create `src/ForgeConflict.App/Views/DashboardPage.xaml` :

```xml
<Page x:Class="ForgeConflict.App.Views.DashboardPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
      xmlns:controls="clr-namespace:ForgeConflict.App.Controls"
      Title="Tableau de bord">

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="32,24,32,32">
        <StackPanel>
            <TextBlock Text="Etat graphique" Style="{StaticResource TitrePage}" />
            <TextBlock x:Name="Sous" Style="{StaticResource Legende}"
                       Text="Lancez une analyse pour connaitre l'etat de la machine." />

            <Grid Margin="0,28,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>

                <Grid Grid.Column="0" Width="200" Height="200">
                    <controls:HealthRing x:Name="Ring" />
                    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                        <TextBlock x:Name="EtatGlobal" Text="-" HorizontalAlignment="Center"
                                   FontFamily="{StaticResource TitreFont}" FontSize="22" FontWeight="SemiBold" />
                        <TextBlock x:Name="Compte" Text="0 / 0 sources" HorizontalAlignment="Center"
                                   Style="{StaticResource Legende}" />
                    </StackPanel>
                </Grid>

                <StackPanel Grid.Column="1" Margin="32,0,0,0" VerticalAlignment="Center">
                    <ui:Button x:Name="ScanButton" Content="Analyser la machine"
                               Appearance="Primary" Padding="20,10" Click="OnScan" />
                    <TextBlock x:Name="Horodatage" Style="{StaticResource Legende}" Margin="0,12,0,0" />
                </StackPanel>
            </Grid>

            <TextBlock Text="Sources d'inventaire" Style="{StaticResource SousTitre}" Margin="0,32,0,8" />
            <ItemsControl x:Name="SourcesListe">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Width="270" Margin="0,0,12,12" Padding="14,12"
                                CornerRadius="6" BorderThickness="1"
                                BorderBrush="{DynamicResource ControlElevationBorderBrush}"
                                Background="{DynamicResource ControlFillColorDefaultBrush}">
                            <StackPanel>
                                <StackPanel Orientation="Horizontal">
                                    <Ellipse Width="9" Height="9" VerticalAlignment="Center"
                                             Fill="{Binding Level, Converter={StaticResource NiveauVersPinceau}}" />
                                    <TextBlock Text="{Binding Source}" Margin="8,0,0,0"
                                               Style="{StaticResource Donnee}" FontWeight="SemiBold" />
                                </StackPanel>
                                <TextBlock Text="{Binding Explanation}" Style="{StaticResource Legende}"
                                           TextWrapping="Wrap" Margin="17,4,0,0" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 4 : Écrire le convertisseur de niveau**

Create `src/ForgeConflict.App/Converters/HealthLevelToBrush.cs` :

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.App.Converters;

/// <summary>Traduit un niveau de sante en pinceau de la palette semantique.</summary>
public sealed class HealthLevelToBrush : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is HealthLevel level
            ? level switch
            {
                HealthLevel.Sain => "Sain",
                HealthLevel.Attention => "Attention",
                HealthLevel.Critique => "Critique",
                _ => "Inconnu",
            }
            : "Inconnu";

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Conversion a sens unique.");
}
```

Puis le déclarer dans `App.xaml`, dans le `ResourceDictionary` :

```xml
                <ResourceDictionary>
                    <converters:HealthLevelToBrush x:Key="NiveauVersPinceau" />
                </ResourceDictionary>
```

avec `xmlns:converters="clr-namespace:ForgeConflict.App.Converters"` sur `<Application>`.

- [ ] **Step 5 : Écrire le code du tableau de bord**

Create `src/ForgeConflict.App/Views/DashboardPage.xaml.cs` :

```csharp
using System.Windows;
using System.Windows.Controls;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.App.Views;

public partial class DashboardPage : Page
{
    private readonly ShellViewModel _viewModel;

    public DashboardPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        Refresh();
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        Sous.Text = "Analyse en cours...";
        try
        {
            await _viewModel.ScanAsync();
            Refresh();
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void Refresh()
    {
        SourcesListe.ItemsSource = _viewModel.Sources;
        Ring.Segments = _viewModel.Sources.Select(s => s.Level).ToList();

        var total = _viewModel.Sources.Count;
        var saines = _viewModel.Sources.Count(s => s.Level == HealthLevel.Sain);

        EtatGlobal.Text = _viewModel.Overall switch
        {
            HealthLevel.Sain => "Sain",
            HealthLevel.Attention => "Partiel",
            HealthLevel.Critique => "Probleme",
            _ => "-",
        };

        Compte.Text = $"{saines} / {total} sources";

        if (_viewModel.Snapshot is { } snapshot)
        {
            Sous.Text = $"{snapshot.MachineName} — {snapshot.OsVersion}";
            Horodatage.Text = $"Derniere analyse : {snapshot.CapturedAt:g}";
        }
    }
}
```

- [ ] **Step 6 : Écrire les pages inventaire et journal**

Create `src/ForgeConflict.App/Views/InventoryPage.xaml` :

```xml
<Page x:Class="ForgeConflict.App.Views.InventoryPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Title="Inventaire">
    <Grid Margin="32,24,32,32">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0">
            <TextBlock Text="Inventaire" Style="{StaticResource TitrePage}" />
            <TextBlock Style="{StaticResource Legende}"
                       Text="Etat brut des dix sources, tel que releve sur la machine." />
        </StackPanel>

        <Border Grid.Row="1" Margin="0,20,0,0" CornerRadius="6" BorderThickness="1"
                BorderBrush="{DynamicResource ControlElevationBorderBrush}"
                Background="{DynamicResource ControlFillColorDefaultBrush}">
            <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Padding="16">
                <TextBlock x:Name="Rapport" Style="{StaticResource Donnee}" />
            </ScrollViewer>
        </Border>
    </Grid>
</Page>
```

Create `src/ForgeConflict.App/Views/InventoryPage.xaml.cs` :

```csharp
using System.Windows.Controls;
using ForgeConflict.App.ViewModels;

namespace ForgeConflict.App.Views;

public partial class InventoryPage : Page
{
    public InventoryPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        Rapport.Text = viewModel.Report;
    }
}
```

Create `src/ForgeConflict.App/Views/LogPage.xaml` — même structure, avec un bouton d'ouverture du
dossier de journaux :

```xml
<Page x:Class="ForgeConflict.App.Views.LogPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
      Title="Journal">
    <Grid Margin="32,24,32,32">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0">
            <TextBlock Text="Journal" Style="{StaticResource TitrePage}" />
            <TextBlock x:Name="Chemin" Style="{StaticResource Legende}" />
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                <ui:Button Content="Rafraichir" Click="OnRefresh" Padding="16,8" />
                <ui:Button Content="Ouvrir le dossier" Click="OnOpenFolder" Padding="16,8" Margin="8,0,0,0" />
            </StackPanel>
        </StackPanel>

        <Border Grid.Row="1" Margin="0,20,0,0" CornerRadius="6" BorderThickness="1"
                BorderBrush="{DynamicResource ControlElevationBorderBrush}"
                Background="{DynamicResource ControlFillColorDefaultBrush}">
            <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Padding="16">
                <TextBlock x:Name="Contenu" Style="{StaticResource Donnee}" />
            </ScrollViewer>
        </Border>
    </Grid>
</Page>
```

Create `src/ForgeConflict.App/Views/LogPage.xaml.cs` :

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ForgeConflict.App.Views;

public partial class LogPage : Page
{
    public LogPage()
    {
        InitializeComponent();
        Chemin.Text = AppPaths.LogDirectory;
        Load();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(AppPaths.LogDirectory)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.LogDirectory,
            UseShellExecute = true,
        });
    }

    private void Load()
    {
        var path = Path.Combine(AppPaths.LogDirectory, "forgeconflict.log");
        if (!File.Exists(path))
        {
            Contenu.Text = "Aucun journal pour cette session.";
            return;
        }

        try
        {
            // Le journal est ouvert en partage : l'application y ecrit pendant la lecture.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Contenu.Text = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            Contenu.Text = $"Journal illisible : {ex.Message}";
        }
    }
}
```

- [ ] **Step 7 : Câbler l'injection de pages**

WPF-UI instancie les pages par `IServiceProvider`. Modify `src/ForgeConflict.App/App.xaml.cs` —
ajouter un fournisseur minimal et le passer à la navigation :

```csharp
    private ServiceProvider? _services;
```

Dans `OnStartup`, après la création du garde-fou :

```csharp
        var collection = new ServiceCollection();
        collection.AddSingleton<ILog>(_log);
        collection.AddSingleton<ShellViewModel>();
        collection.AddSingleton<MainWindow>();
        collection.AddTransient<DashboardPage>();
        collection.AddTransient<InventoryPage>();
        collection.AddTransient<LogPage>();
        _services = collection.BuildServiceProvider();

        _services.GetRequiredService<MainWindow>().Show();
```

Et retirer `StartupUri="MainWindow.xaml"` de `App.xaml`, la fenêtre étant désormais créée par le
conteneur.

- [ ] **Step 8 : Compiler et vérifier**

Run : `dotnet build ForgeConflict.sln --nologo`
Expected : `0 Erreur(s)`, `0 Avertissement(s)`.

- [ ] **Step 9 : Commit**

```bash
git add -A
git commit -m "Add Fluent shell with dashboard, inventory and log pages"
```

---

### Task 5 : Vérification et publication

- [ ] **Step 1 : Lancer toute la suite**

Run : `dotnet build ForgeConflict.sln --nologo; dotnet vstest tests\ForgeConflict.Tests\bin\x64\Debug\net8.0-windows\ForgeConflict.Tests.dll --logger:"console;verbosity=minimal"`
Expected : `Failed: 0`.

- [ ] **Step 2 : Passer la version à 0.4.0**

Modify `Directory.Build.props` : `<Version>0.4.0</Version>`

- [ ] **Step 3 : Publier**

Run : `powershell -ExecutionPolicy Bypass -File .\build.ps1`
Expected : `ForgeConflict 0.4.0`, `0 fichier(s) annexe(s)`.

- [ ] **Step 4 : Vérifier par pilotage de l'interface**

Piloter la fenêtre par UI Automation et confirmer :
1. La fenêtre s'ouvre avec le matériau Mica et la barre de titre étendue ;
2. « Analyser la machine » remplit l'anneau de dix segments verts ;
3. L'état global affiche `Sain` et `10 / 10 sources` ;
4. La page Inventaire affiche le rapport en police monospace ;
5. La page Journal affiche les lignes de la session en cours ;
6. Aucune exception dans `.logs/forgeconflict.log`.

- [ ] **Step 5 : Commit et push**

```bash
git add -A
git commit -m "Verify Fluent shell and bump to 0.4.0"
git push origin lot1a-socle
```

---

## Ce que ce lot ne fait pas

Aucune règle, aucune correction. L'interface présente l'inventaire ; le lot 4 y ajoutera les
conflits détectés et leurs explications.
