using Microsoft.UI.Xaml.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ClickToImages.App.Converters;

// "1 page" / "12 pages" for a document's group header and its list row.
public sealed class PageCountConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader = new();

    public object Convert(object value, Type targetType, object parameter, string language) =>
        Describe(value is int count ? count : 0);

    public static string Describe(int count) =>
        count == 1
            ? ResourceLoader.GetString("PageCountSingular")
            : string.Format(ResourceLoader.GetString("PageCountPluralFormat"), count);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
