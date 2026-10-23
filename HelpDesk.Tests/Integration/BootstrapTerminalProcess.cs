using System.Diagnostics;
using System.Text;
using HelpDesk.AdminBootstrap;

namespace HelpDesk.Tests.Integration;

// PTY Linux real: el ejecutable mantiene la exigencia de terminal, sin flags ni bypass de Testing.
internal sealed class BootstrapTerminalProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StringBuilder output = new();
    private readonly object gate = new();
    private TaskCompletionSource changed = NewSignal();
    private readonly Task pump;
    private readonly Task<string> errors;

    public BootstrapTerminalProcess(string validatedConnectionString)
    {
        // Validar de nuevo antes de suministrar la única conexión admitida por estos tests.
        validatedConnectionString = PostgreSqlTestSettings.Validate(validatedConnectionString);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "HelpDesk.NET.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("No se encontró la solución para ejecutar la CLI de pruebas.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(root.FullName, "HelpDesk.AdminBootstrap", "bin", configuration, "net10.0", "HelpDesk.AdminBootstrap.dll");
        if (!File.Exists(cli)) throw new InvalidOperationException("Compila la solución antes de probar los procesos CLI.");
        var start = new ProcessStartInfo("script")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-qefc");
        // No hay secretos en argumentos, ficheros o transcripciones. stty evita eco incluso al arrancar.
        start.ArgumentList.Add("stty -echo; exec dotnet '" + cli.Replace("'", "'\\''", StringComparison.Ordinal) + "'");
        start.ArgumentList.Add("/dev/null");
        start.Environment[BootstrapConnection.EnvironmentVariable] = validatedConnectionString;
        process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar la terminal CLI de pruebas.");
        errors = process.StandardError.ReadToEndAsync();
        pump = PumpAsync();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task PumpAsync()
    {
        var buffer = new char[256];
        int count;
        while ((count = await process.StandardOutput.ReadAsync(buffer)) > 0)
        {
            lock (gate)
            {
                output.Append(buffer, 0, count);
                changed.TrySetResult();
                changed = NewSignal();
            }
        }
        lock (gate) changed.TrySetResult();
    }

    private async Task SendAfterPromptAsync(string prompt, string text)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            Task signal;
            lock (gate)
            {
                if (output.ToString().Contains(prompt, StringComparison.Ordinal)) break;
                signal = changed.Task;
            }
            if (process.HasExited) throw new InvalidOperationException("La CLI terminó antes de solicitar los datos esperados; no se muestran salidas sensibles.");
            await signal.WaitAsync(timeout.Token);
        }
        await process.StandardInput.WriteLineAsync(text);
        await process.StandardInput.FlushAsync();
    }

    public Task ConfirmAsync(string email, string name, string password) =>
        EnterAsync(email, name, password, BootstrapService.Confirmation);

    public async Task EnterAsync(string email, string name, string password, string confirmation)
    {
        await SendAfterPromptAsync("Escribe el nombre exacto de la base de destino: ", PostgreSqlTestSettings.DatabaseName);
        await SendAfterPromptAsync("Email de la cuenta nueva: ", email);
        await SendAfterPromptAsync("Nombre: ", name);
        await SendAfterPromptAsync("Contraseña (oculta): ", password);
        await SendAfterPromptAsync("Repite la contraseña (oculta): ", password);
        await SendAfterPromptAsync($"Escribe exactamente {BootstrapService.Confirmation}: ", confirmation);
    }

    public async Task<(int ExitCode, string Output)> CompleteAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        await pump;
        var error = await errors;
        lock (gate) return (process.ExitCode, output + error);
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await pump;
        await errors;
        process.Dispose();
    }
}
