using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MineEditor.ViewModels;

namespace MineEditor;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Gets the app-wide state.</summary>
    public static ShellViewModel Shell { get; } = new();

    /// <summary>Gets the main window, for dialogs and pickers.</summary>
    public static MainWindow MainWindow { get; private set; } = null!;

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();

        // A world folder or level.dat on the command line is opened straight away.
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 1 && (Directory.Exists(commandLine[1]) || File.Exists(commandLine[1])))
        {
            await MainWindow.OpenWorldAsync(commandLine[1]);
        }
    }

    // Report unexpected errors instead of closing the app, so unsaved edits aren't lost.
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine(e.Exception);
        MainWindow?.ShowStatus(InfoBarSeverity.Error, "Something went wrong", $"{e.Exception.GetType().Name}: {e.Message}");
    }
}
