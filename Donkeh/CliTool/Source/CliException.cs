namespace Donkeh.Cli;

/// <summary>A user-facing command failure that should print a clean message.</summary>
internal sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }
}
