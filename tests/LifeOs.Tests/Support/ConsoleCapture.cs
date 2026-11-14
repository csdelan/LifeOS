using System.Text;

namespace LifeOs.Tests;

/// <summary>
/// What code running in one async flow writes to the console, for a test that runs <c>bsk</c>
/// in-process. The console is the process's, and the classes run alongside each other, so the
/// writer installed once sends each write to the flow that asked for it, or on to the console.
/// </summary>
internal static class ConsoleCapture
{
    private static readonly AsyncLocal<StringWriter?> Out = new();
    private static readonly AsyncLocal<StringWriter?> Error = new();
    private static readonly Lock Gate = new();
    private static bool installed;

    /// <summary>Runs <paramref name="run"/>, and answers its exit code and what it printed.</summary>
    public static async Task<(int Exit, string Out, string Error)> RunAsync(Func<Task<int>> run)
    {
        Install();
        var output = new StringWriter();
        var error = new StringWriter();
        Out.Value = output;
        Error.Value = error;
        try
        {
            var exit = await run();
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Out.Value = null;
            Error.Value = null;
        }
    }

    private static void Install()
    {
        lock (Gate)
        {
            if (installed)
            {
                return;
            }

            Console.SetOut(new Forward(Console.Out, () => Out.Value));
            Console.SetError(new Forward(Console.Error, () => Error.Value));
            installed = true;
        }
    }

    private sealed class Forward(TextWriter console, Func<TextWriter?> flow) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;

        private TextWriter Target => flow() ?? console;

        public override void Write(char value) => Target.Write(value);

        public override void Write(string? value) => Target.Write(value);

        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);

        public override void WriteLine() => Target.WriteLine();

        public override void WriteLine(string? value) => Target.WriteLine(value);

        public override void Flush() => Target.Flush();
    }
}
