using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PortTerminator.Core.Models;

namespace PortTerminator.UI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString() == "Invert";
        var visible = value is true;
        if (invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel level)
        {
            return level switch
            {
                RiskLevel.Low => new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)),
                RiskLevel.Medium => new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 173, 20)),
                RiskLevel.High => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 144, 255)),
                LogLevel.Success => new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)),
                LogLevel.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 173, 20)),
                LogLevel.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class LogLevelToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogLevel level ? level switch
        {
            LogLevel.Info => "信息",
            LogLevel.Success => "成功",
            LogLevel.Warning => "警告",
            LogLevel.Error => "错误",
            _ => "信息"
        } : "信息";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NavigationPageToNavStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var normal = System.Windows.Application.Current.FindResource("NavButton") as Style;
        var active = System.Windows.Application.Current.FindResource("NavButtonActive") as Style;

        if (value is NavigationPage current && parameter is string pageName
            && Enum.TryParse<NavigationPage>(pageName, out var target))
        {
            return current == target ? active ?? normal! : normal!;
        }

        return normal!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NavPageMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is NavigationPage current
            && values[1] is string tag
            && Enum.TryParse<NavigationPage>(tag, out var target))
        {
            return current == target;
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NavigationPageToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NavigationPage page && parameter is string pageName
            && Enum.TryParse<NavigationPage>(pageName, out var target))
        {
            return page == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class HighRiskForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel level && level == RiskLevel.High)
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 38, 38));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
