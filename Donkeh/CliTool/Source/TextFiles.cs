using System.Text;

namespace Donkeh.Cli;

/// <summary>Small file-writing helpers used by generators.</summary>
internal static class TextFiles
{
    /// <summary>Writes UTF-8 text with the platform's newline sequence.</summary>
    public static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var newline = Environment.NewLine;
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", newline, StringComparison.Ordinal);

        File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Reads a file and detects its line-ending style.</summary>
    public static (string Text, string NewLine) Read(string path)
    {
        var text = File.ReadAllText(path);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return (text, newLine);
    }
}
