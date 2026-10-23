using System.Text;

namespace HelpDesk.AdminBootstrap;

public interface IBootstrapConsole
{
    bool IsInteractive { get; }
    void WriteLine(string message);
    string? ReadLine(string prompt);
    string? ReadSecret(string prompt);
}

public sealed class BootstrapConsole : IBootstrapConsole
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;
    public void WriteLine(string message) => Console.WriteLine(message);
    public string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine();
    }

    public string? ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var text = new StringBuilder();
        var previous = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape || key.KeyChar is '\u0003' or '\u0004') return null;
                if (key.Key == ConsoleKey.Enter) return text.ToString();
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (text.Length > 0) text.Length--;
                }
                else if (key.KeyChar != '\0') text.Append(key.KeyChar);
            }
        }
        finally
        {
            Console.TreatControlCAsInput = previous;
            Console.WriteLine();
            text.Clear();
        }
    }
}
