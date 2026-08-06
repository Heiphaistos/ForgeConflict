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
