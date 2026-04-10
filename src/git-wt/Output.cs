internal static class Output
{
    internal static bool IsPlain { get; private set; }
    internal static bool UseColor { get; private set; }

    static Output()
    {
        bool noColor = Console.IsOutputRedirected
            || Environment.GetEnvironmentVariable("NO_COLOR") != null;
        IsPlain = noColor;
        UseColor = !noColor;
    }

    internal static void Init(bool forceColor, bool forceNoColor)
    {
        if (forceColor)
        {
            IsPlain = false;
            UseColor = true;
        }
        else if (forceNoColor)
        {
            IsPlain = true;
            UseColor = false;
        }
    }

    internal static string Green(string text) => UseColor ? $"\x1b[32m{text}\x1b[0m" : text;
    internal static string Red(string text) => UseColor ? $"\x1b[31m{text}\x1b[0m" : text;
    internal static string Dim(string text) => UseColor ? $"\x1b[90m{text}\x1b[0m" : text;
}
