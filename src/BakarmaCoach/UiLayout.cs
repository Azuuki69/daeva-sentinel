using System.IO;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BakarmaCoach;

public static class UiLayout
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void DarkCaption(Window window)
    {
        int enabled = 1, caption = 0x001A100A;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
    }
    public static BitmapFrame Icon() => BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "ui", "sentinel.ico")));
    public static Grid Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ui", name);
        return (Grid)XamlReader.Parse(File.ReadAllText(path), new ParserContext { BaseUri = new Uri(path) });
    }
}
