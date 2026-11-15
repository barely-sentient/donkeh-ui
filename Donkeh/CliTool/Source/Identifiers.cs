using System.Text;

namespace Donkeh.Cli;

/// <summary>Identifiers and CSS naming helpers used by generated files.</summary>
internal static class Identifiers
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>Converts a user-supplied component name to a PascalCase identifier.</summary>
    public static string ComponentName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var words = value.Split(
            [' ', '-', '_', '.', '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var builder = new StringBuilder();

        foreach (var word in words)
        {
            builder.Append(char.ToUpperInvariant(word[0]));
            builder.Append(word.AsSpan(1));
        }

        if (builder.Length == 0)
        {
            builder.Append("Component");
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        var name = builder.ToString();

        return Keywords.Contains(name) ? name + "Component" : name;
    }

    /// <summary>Converts a name to a dotted, valid namespace root.</summary>
    public static string Namespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return "DonkehApp";
        }

        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(ComponentName(part));
        }

        return builder.ToString();
    }

    /// <summary>Converts a PascalCase name to a CSS class such as <c>my-component</c>.</summary>
    public static string KebabCase(string value)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (char.IsUpper(character))
            {
                var previousIsLower = i > 0 && char.IsLower(value[i - 1]);
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (builder.Length > 0 && (previousIsLower || nextIsLower))
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "component" : builder.ToString().Trim('-');
    }
}
