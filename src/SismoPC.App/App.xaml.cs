using Microsoft.UI.Xaml;

namespace SismoPC.Ui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Fenêtre principale, pour la navigation depuis les pages.</summary>
    public static MainWindow? Window { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();

        // Ouverture depuis une notification : « SismoPC.exe sismopc://episode/42 » (protocole enregistré par l'installeur).
        Window.NavigateFromArguments(Environment.GetCommandLineArgs());
    }
}
