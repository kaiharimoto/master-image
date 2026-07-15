using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace MasterImage.App;

public partial class App : Application
{
    private const string MutexName = "MasterImage.SingleInstance";
    private const string PipeName = "MasterImage.OpenRequest";

    private Mutex? _mutex;
    private NamedPipeServerStream? _pipeServer;

    public static event Action<string>? OpenRequested;

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
