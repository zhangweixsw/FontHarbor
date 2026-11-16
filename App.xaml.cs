using System.IO;
using System.Windows;

namespace FontHarbor;

public partial class App : Application
{
    private bool _errorDialogOpen;
    public static bool SuppressMissingFontErrors { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (SuppressMissingFontErrors && IsMissingFileError(args.Exception))
            {
                args.Handled = true;
                return;
            }
            if (_errorDialogOpen)
            {
                args.Handled = true;
                return;
            }
            _errorDialogOpen = true;
            try { MessageBox.Show(args.Exception.Message, "发生错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { _errorDialogOpen = false; }
            args.Handled = true;
        };
        base.OnStartup(e);
    }

    private static bool IsMissingFileError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is FileNotFoundException || current.Message.Contains("Unable to find the specified file", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
