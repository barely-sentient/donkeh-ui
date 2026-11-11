using System.Reflection;

namespace Ignite.Windowing
{
    /// <summary>
    /// Loads and merges stylesheets declared through <see cref="StylesheetAttribute"/>.
    /// </summary>
    public static class Stylesheet
    {
        public static string Load() => Ignite.UILab.Stylesheet.Load();

        public static string Load(Assembly assembly) => Ignite.UILab.Stylesheet.Load(assembly);

        public static string Load(params Assembly[] assemblies) => Ignite.UILab.Stylesheet.Load(assemblies);
    }
}