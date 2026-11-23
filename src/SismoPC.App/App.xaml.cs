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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("SismoPC.Ui." + SismoPC.Core.Ipc.PipeProtocol.PipeName);
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        Window = new MainWindow();
        instance.Activated += (_, activation) => Window.DispatcherQueue.TryEnqueue(() =>
        {
            if (Window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();
            Window.Activate();
            if (activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
                Window.NavigateFromArguments([protocol.Uri.ToString()]);
            else if (activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
                Window.NavigateFromArguments(System.Text.RegularExpressions.Regex.Matches(launch.Arguments, @"sismopc://[^\s""']+").Select(m => m.Value));
        });
        Window.Activate();

        // Ouverture depuis une notification : « SismoPC.exe sismopc://episode/42 » (protocole enregistré par l'installeur).
        Window.NavigateFromArguments(Environment.GetCommandLineArgs());
    }
}
