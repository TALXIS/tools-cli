using System.Net;
using TALXIS.CLI.Data.DataServer;

namespace TALXIS.CLI.Data;

public class DataTransformationServer
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private bool _isRunning;

    public DataTransformationServer(int port)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        _listener.Start();
        Console.WriteLine($"Data Transformation server running on http://localhost:{_port}/");
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            var context = await _listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context));
        }
        _listener.Stop();
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            // Try all controllers in order
            if (await ComputePrimaryKeyController.TryHandle(context))
                return;

            // Not found
            context.Response.StatusCode = 404;
            await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Not found"));
            context.Response.Close();
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"Error: {ex.Message}"));
            context.Response.Close();
        }
    }
}
