#pragma warning disable CS1591

namespace Donkeh.UILab
{
    // CSX attributes are emitted with the casing written in the .cx file. These
    // aliases let route tables use the familiar React Router-style names while
    // the C# API keeps its PascalCase properties.
    public partial class AppRouterProps
    {
        public string? initialPath
        {
            get => InitialPath;
            init => InitialPath = value ?? "/";
        }
    }

    public partial class AppRoutesProps
    {
        public Node? fallback
        {
            get => Fallback;
            init => Fallback = value;
        }
    }

    public partial class RoutesProps
    {
        public Node? fallback
        {
            get => Fallback;
            init => Fallback = value;
        }
    }

    public partial class RouteProps
    {
        public string? path
        {
            get => Path;
            init => Path = value ?? "/";
        }

        public bool index
        {
            get => Index;
            init => Index = value;
        }

        public Node? element
        {
            get => Element;
            init => Element = value;
        }

        public Func<Node?>? content
        {
            get => Content;
            init => Content = value;
        }
    }

    public partial class LinkProps
    {
        public string? to
        {
            get => To;
            init => To = value ?? "/";
        }

        public string? activeClassName
        {
            get => ActiveClassName;
            init => ActiveClassName = value;
        }
    }
}
