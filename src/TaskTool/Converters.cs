using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TaskTool.Models;

namespace TaskTool;

public class StatusBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush AFazer = Frozen(0x9A, 0x9A, 0x9A);
    public static readonly SolidColorBrush EmAndamento = Frozen(0x4C, 0xC2, 0xFF);
    public static readonly SolidColorBrush Bloqueado = Frozen(0xFF, 0x6B, 0x6B);
    public static readonly SolidColorBrush AFinalizar = Frozen(0xB8, 0x9C, 0xFF);
    public static readonly SolidColorBrush Concluida = Frozen(0x6C, 0xCB, 0x5F);

    public static SolidColorBrush De(StatusTarefa s) => s switch
    {
        StatusTarefa.EmAndamento => EmAndamento,
        StatusTarefa.Bloqueado => Bloqueado,
        StatusTarefa.AFinalizar => AFinalizar,
        StatusTarefa.Concluida => Concluida,
        _ => AFazer,
    };

    /// <summary>Cores dos itens: Pendente cinza, Em andamento azul, Aguardando amarelo, Feito verde.</summary>
    public static readonly SolidColorBrush Aguardando = Frozen(0xFF, 0xC8, 0x3D);

    public static SolidColorBrush De(StatusItem s) => s switch
    {
        StatusItem.EmAndamento => EmAndamento,
        StatusItem.Aguardando => Aguardando,
        StatusItem.Feito => Concluida,
        _ => AFazer,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StatusTarefa s => De(s),
        StatusItem s => De(s),
        _ => AFazer,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

/// <summary>RadioButton.IsChecked ↔ enum. ConverterParameter = nome do valor.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string s ? Enum.Parse(targetType, s) : Binding.DoNothing;
}

public class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch
        {
            bool x => x,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
