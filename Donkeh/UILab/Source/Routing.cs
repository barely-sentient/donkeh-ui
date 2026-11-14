using System;
using System.Collections.Generic;
using static Donkeh.UILab._;

namespace Donkeh.UILab
{
    /// <summary>
    /// Shared router contexts and path helpers. An <see cref="AppRouter"/> publishes
    /// these values to its subtree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The router is deliberately host-agnostic: it stores a path string and
    /// re-renders matching route content. It does not touch the URL bar, browser
    /// history or the network. A host can persist the current path or drive it from
    /// its own navigation model through <see cref="SetPage"/>.
    /// </para>
    /// <para>
    /// Paths are normalised to start with <c>/</c>, collapse duplicate slashes and
    /// drop a trailing slash (except for the root). A route ending in <c>/*</c>
    /// matches that prefix and its descendants; <c>*</c> alone is a catch-all.
    /// </para>
    /// </remarks>
    public static class Router
    {
        /// <summary>The current normalised path.</summary>
        public static readonly Context<string> Path = new("/");

        /// <summary>
        /// Sets the current page. Components obtain it with
        /// <c>UseContext(Router.SetPage)</c>.
        /// </summary>
        public static readonly Context<Action<string>> SetPage = new(static _ => { });

        /// <summary>Normalises a route or page path.</summary>
        /// <param name="path">The path to normalise; null or whitespace becomes <c>/</c>.</param>
        /// <returns>A leading-slash path, or <c>*</c> for the catch-all route.</returns>
        public static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            path = path.Trim();

            if (path == "*")
            {
                return "*";
            }

            var query = path.IndexOfAny(['?', '#']);

            if (query >= 0)
            {
                path = path[..query];
            }

            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }

            var slash = path.IndexOf("//", StringComparison.Ordinal);

            while (slash >= 0)
            {
                path = path[..slash] + path[(slash + 1)..];
                slash = path.IndexOf("//", StringComparison.Ordinal);
            }

            if (path.Length > 1)
            {
                path = path.TrimEnd('/');

                if (path.Length == 0)
                {
                    return "/";
                }
            }

            return path;
        }

        /// <summary>Tests whether a route path matches the current path.</summary>
        /// <param name="routePath">The declared route path (may end in <c>/*</c> or be <c>*</c>).</param>
        /// <param name="currentPath">The current page path.</param>
        /// <returns><see langword="true"/> when the route is active.</returns>
        public static bool Matches(string? routePath, string? currentPath) =>
            MatchesNormalized(routePath, Normalize(currentPath));

        /// <summary>Tests a route against an already normalised current path.</summary>
        /// <param name="routePath">The declared route path.</param>
        /// <param name="currentPath">A path returned by <see cref="Normalize"/>.</param>
        /// <returns><see langword="true"/> when the route is active.</returns>
        internal static bool MatchesNormalized(string? routePath, string currentPath)
        {
            var route = Normalize(routePath);

            if (route == "*")
            {
                return true;
            }

            if (route.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = route[..^2];
                return currentPath.Equals(prefix, StringComparison.Ordinal)
                    || currentPath.StartsWith(prefix + "/", StringComparison.Ordinal);
            }

            return route.Equals(currentPath, StringComparison.Ordinal);
        }

        /// <summary>
        /// Tests whether a link should be marked active. A link is active on its
        /// exact path and on descendant paths, except for the root link.
        /// </summary>
        /// <param name="linkPath">The link target.</param>
        /// <param name="currentPath">The current page path.</param>
        /// <returns><see langword="true"/> when the link represents the current section.</returns>
        public static bool IsActive(string? linkPath, string? currentPath) =>
            IsActiveNormalized(linkPath, Normalize(currentPath));

        /// <summary>Tests a link against an already normalised current path.</summary>
        /// <param name="linkPath">The link target.</param>
        /// <param name="currentPath">A path returned by <see cref="Normalize"/>.</param>
        /// <returns><see langword="true"/> when the link represents the current section.</returns>
        internal static bool IsActiveNormalized(string? linkPath, string currentPath)
        {
            var link = Normalize(linkPath);

            if (link == "/")
            {
                return currentPath == "/";
            }

            return link == currentPath
                || currentPath.StartsWith(link + "/", StringComparison.Ordinal);
        }
    }

    /// <summary>Props for <see cref="AppRouter"/>.</summary>
    public class AppRouterProps : Props
    {
        /// <summary>
        /// Gets the initially selected path. Defaults to <c>/</c> and is read when
        /// the router mounts; later prop changes do not reset the current page.
        /// </summary>
        public string InitialPath { get; init; } = "/";

        /// <summary>Gets the child nodes. Usually one <see cref="AppRoutes"/>.</summary>
        public Node?[]? Children { get; init; }
    }

    /// <summary>
    /// Router provider. It owns the current page path and publishes
    /// <see cref="Router.Path"/> and <see cref="Router.SetPage"/> to its subtree.
    /// </summary>
    /// <example>
    /// <code>
    /// C&lt;AppRouter&gt;(new AppRouterProps
    /// {
    ///     InitialPath = "/",
    ///     Children =
    ///     [
    ///         C&lt;AppRoutes&gt;(new AppRoutesProps
    ///         {
    ///             Children =
    ///             [
    ///                 C&lt;Route&gt;(new RouteProps { Path = "/", Children = [Text("Home")] }),
    ///                 C&lt;Route&gt;(new RouteProps { Path = "/about", Children = [Text("About")] })
    ///             ]
    ///         })
    ///     ]
    /// });
    /// </code>
    /// </example>
    public sealed class AppRouter : Component<AppRouterProps>
    {
        /// <inheritdoc/>
        public override Node Render()
        {
            var (path, setPath) = State(Router.Normalize(Props.InitialPath));

            void SetPage(string next) => setPath(Router.Normalize(next));

            return Provider(
                Router.Path,
                path,
                Provider(
                    Router.SetPage,
                    (Action<string>)SetPage,
                    F(Props.Children ?? [])));
        }
    }

    /// <summary>Props for <see cref="AppRoutes"/>.</summary>
    public class AppRoutesProps : Props
    {
        /// <summary>Gets the route declarations to search, in order.</summary>
        public Node?[]? Children { get; init; }

        /// <summary>Gets the content rendered when no route matches.</summary>
        public Node? Fallback { get; init; }
    }

    /// <summary>
    /// Route table. Renders the first matching <see cref="Route"/> child, or
    /// <see cref="AppRoutesProps.Fallback"/> when nothing matches.
    /// </summary>
    public sealed class AppRoutes : Component<AppRoutesProps>
    {
        /// <inheritdoc/>
        public override Node Render()
        {
            var current = UseContext(Router.Path);
            var route = FindRoute(Props.Children, current);

            if (route is not null)
            {
                return route;
            }

            return Props.Fallback ?? F();
        }

        private static Node? FindRoute(Node?[]? children, string current)
        {
            if (children is null)
            {
                return null;
            }

            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child is ComponentNode
                    {
                        ComponentType: var type,
                        Props: RouteProps props
                    }
                    && type == typeof(Route)
                    && Router.MatchesNormalized(props.Path, current))
                {
                    return child;
                }

                if (child is FragmentNode fragment)
                {
                    var nested = FindRoute(fragment.VdomChildren, current);

                    if (nested is not null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>Props for <see cref="Route"/>.</summary>
    public class RouteProps : Props
    {
        /// <summary>Gets the route path, for example <c>/</c>, <c>/about</c> or <c>/docs/*</c>.</summary>
        public string Path { get; init; } = "/";

        /// <summary>Gets the content rendered when the route matches.</summary>
        public Node?[]? Children { get; init; }
    }

    /// <summary>Declares content for one path.</summary>
    public sealed class Route : Component<RouteProps>
    {
        /// <inheritdoc/>
        public override Node Render()
        {
            var current = UseContext(Router.Path);
            return Router.MatchesNormalized(Props.Path, current)
                ? F(Props.Children ?? [])
                : F();
        }
    }

    /// <summary>Props for <see cref="Link"/>.</summary>
    public class LinkProps : Props
    {
        /// <summary>Gets the destination path.</summary>
        public string To { get; init; } = "/";

        /// <summary>Gets the class added while the link is active. Defaults to <c>active</c>.</summary>
        public string? ActiveClassName { get; init; } = "active";

        /// <summary>Gets the link content.</summary>
        public Node?[]? Children { get; init; }
    }

    /// <summary>
    /// In-app navigation link. It renders an anchor and updates
    /// <see cref="Router.SetPage"/> on click instead of relying on browser
    /// navigation, which does not exist in Ignite.
    /// </summary>
    public sealed class Link : Component<LinkProps>
    {
        /// <inheritdoc/>
        public override Node Render()
        {
            var current = UseContext(Router.Path);
            var setPage = UseContext(Router.SetPage);
            var active = Router.IsActiveNormalized(Props.To, current);

            var className = string.IsNullOrWhiteSpace(Props.ClassName)
                ? string.Empty
                : Props.ClassName!;

            if (active && !string.IsNullOrWhiteSpace(Props.ActiveClassName))
            {
                className = className.Length == 0
                    ? Props.ActiveClassName!
                    : className + " " + Props.ActiveClassName;
            }

            return H("a", new Props
            {
                Href = Props.To,
                ClassName = className,
                OnClick = () =>
                {
                    Props.OnClick?.Invoke();
                    setPage(Props.To);
                }
            }, Props.Children ?? []);
        }
    }

    /// <summary>Convenience factories for the router components.</summary>
    /// <remarks>
    /// Import this class statically to write a route table without repeating the
    /// component-generic syntax:
    /// <code>
    /// using static Donkeh.UILab.Routing;
    ///
    /// AppRouter("/",
    ///     AppRoutes(
    ///         Route("/", Text("Home")),
    ///         Route("/about", Text("About"))));
    /// </code>
    /// </remarks>
    public static class Routing
    {
        /// <summary>Creates an <see cref="AppRouter"/> provider.</summary>
        /// <param name="initialPath">The initially selected path.</param>
        /// <param name="children">Router children, normally one <see cref="AppRoutes"/>.</param>
        /// <returns>The configured router component.</returns>
        public static Node AppRouter(string initialPath, params Node?[] children) =>
            C<AppRouter, AppRouterProps>(new AppRouterProps
            {
                InitialPath = initialPath,
                Children = children
            });

        /// <summary>Creates an <see cref="AppRoutes"/> route table.</summary>
        /// <param name="routes">Route declarations in priority order.</param>
        /// <returns>The configured route table component.</returns>
        public static Node AppRoutes(params Node?[] routes) =>
            C<AppRoutes, AppRoutesProps>(new AppRoutesProps { Children = routes });

        /// <summary>Creates a <see cref="Route"/> declaration.</summary>
        /// <param name="path">The route path.</param>
        /// <param name="children">Content rendered when the route matches.</param>
        /// <returns>The configured route component.</returns>
        public static Node Route(string path, params Node?[] children) =>
            C<Route, RouteProps>(new RouteProps { Path = path, Children = children });

        /// <summary>Creates a <see cref="Link"/>.</summary>
        /// <param name="to">The destination path.</param>
        /// <param name="children">Link content.</param>
        /// <returns>The configured link component.</returns>
        public static Node Link(string to, params Node?[] children) =>
            C<Link, LinkProps>(new LinkProps { To = to, Children = children });
    }
}
