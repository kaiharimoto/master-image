using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Velopack;

namespace MasterImage.App;

public partial class App : Application
{
    private const string MutexName = "MasterImage.SingleInstance";
    private const string PipeName = "MasterImage.OpenRequest";

    private Mutex? _mutex;
    private NamedPipeServerStream? _pipeServer;

    public static event Action<string>? OpenRequested;

    // Velopack has to run before WPF does: when it's mid-install, mid-update or uninstalling, it
    // needs to do its work and exit without ever putting a window on screen. Owning Main is how it
    // gets that chance — hence the ApplicationDefinition removal in the csproj.
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => FileAssociations.Register(StubExePath()))
            .OnAfterUpdateFastCallback(_ => FileAssociations.Register(StubExePath()))
            .OnBeforeUninstallFastCallback(_ => FileAssociations.Unregister())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    // The path file associations must point at: %LocalAppData%\MasterImage\MasterImage.exe, the
    // stub Velopack keeps at the root of the install which forwards to whatever is in current\.
    //
    // This must never be the running exe's own path. Velopack replaces the entire current\ folder on
    // every update, so an association pointing there would break the first time the app updated
    // itself. Falls back to this exe for dev builds, where no stub exists.
    private static string StubExePath()
    {
        string self = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MasterImage.App.exe");
        var installDir = new DirectoryInfo(AppContext.BaseDirectory);

        if (installDir.Name.Equals("current", StringComparison.OrdinalIgnoreCase) && installDir.Parent is not null)
        {
            string stub = Path.Combine(installDir.Parent.FullName, "MasterImage.exe");
            if (File.Exists(stub))
            {
                return stub;
            }
        }

        return self;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? requestedPath = e.Args.Length > 0 ? e.Args[0] : null;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (requestedPath is not null)
            {
                ForwardToRunningInstance(requestedPath);
            }
            Shutdown();
            return;
        }

        StartPipeServer();

        var window = new MainWindow(requestedPath);
        window.Show();
    }

    private static void ForwardToRunningInstance(string path)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(timeout: 1000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.Write(path);
    }

    private void StartPipeServer()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                _pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In);
                await _pipeServer.WaitForConnectionAsync();
                using var reader = new StreamReader(_pipeServer);
                string path = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Current.Dispatcher.Invoke(() => OpenRequested?.Invoke(path));
                }
                _pipeServer.Dispose();
            }
        });
    }
}
