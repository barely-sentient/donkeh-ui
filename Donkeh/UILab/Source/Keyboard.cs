using System;
using System.Collections.Generic;

namespace Donkeh.UILab
{
    /// <summary>
    /// Modifier keys held while a key was pressed, as reported by the host.
    /// </summary>
    /// <remarks>
    /// The flag values match the numbers the browser bridge sends for a
    /// <c>keydown</c> event, so a host that receives modifiers in that form can
    /// cast straight to this type.
    /// </remarks>
    [Flags]
    public enum KeyModifiers
    {
        /// <summary>No modifier was held.</summary>
        None = 0,

        /// <summary>Ctrl (Control) was held.</summary>
        Control = 1,

        /// <summary>Shift was held.</summary>
        Shift = 2,

        /// <summary>Alt was held.</summary>
        Alt = 4,

        /// <summary>
        /// The platform key was held: Windows on Windows, Command on macOS and
        /// Super on Linux.
        /// </summary>
        Meta = 8,
    }

    /// <summary>
    /// Builds the key text that <c>keydown</c> handlers receive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain press keeps the bare key name, for example <c>Enter</c> or
    /// <c>B</c>. Held modifiers are prefixed in the fixed order <c>Ctrl</c>,
    /// <c>Shift</c>, <c>Alt</c>, <c>Meta</c>, so Ctrl+Shift+Z arrives as
    /// <c>Ctrl+Shift+Z</c>.
    /// </para>
    /// <para>
    /// A modifier pressed on its own reports the key name without a prefix
    /// (<c>ShiftLeft</c>), which keeps those events distinct. Handlers compare
    /// against the literal text or build it with
    /// <see cref="Format(string, KeyModifiers)"/>; shortcut hints in the UI should
    /// use the same helper so a shortcut is spelled one way everywhere.
    /// </para>
    /// </remarks>
    public static class KeyChord
    {
        /// <summary>Modifier prefixes in the order they are written.</summary>
        private static readonly (KeyModifiers Modifier, string Name)[] Order =
        [
            (KeyModifiers.Control, "Ctrl"),
            (KeyModifiers.Shift, "Shift"),
            (KeyModifiers.Alt, "Alt"),
            (KeyModifiers.Meta, "Meta"),
        ];

        /// <summary>
        /// The key names that represent a modifier key itself. Both the desktop
        /// spelling (<c>SuperLeft</c>) and the browser spelling (<c>Meta</c>) are
        /// recognised so neither host produces <c>Shift+ShiftLeft</c>.
        /// </summary>
        private static readonly HashSet<string> ModifierKeys = new(StringComparer.Ordinal)
        {
            "ControlLeft",
            "ControlRight",
            "ShiftLeft",
            "ShiftRight",
            "AltLeft",
            "AltRight",
            "SuperLeft",
            "SuperRight",
            "MetaLeft",
            "MetaRight",
            "Control",
            "Shift",
            "Alt",
            "Meta",
        };

        /// <summary>Formats a key name together with the modifiers held with it.</summary>
        /// <param name="key">The key name, such as <c>C</c>.</param>
        /// <param name="modifiers">The modifiers held when the key was pressed.</param>
        /// <returns>
        /// The key text a <c>keydown</c> handler receives: <c>C</c> on its own,
        /// <c>Ctrl+Shift+C</c> with modifiers, or an empty string for an empty key.
        /// </returns>
        /// <remarks>
        /// Modifier keys themselves are never prefixed, so Shift reports
        /// <c>ShiftLeft</c> rather than <c>Shift+ShiftLeft</c>.
        /// </remarks>
        public static string Format(string key, KeyModifiers modifiers)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (modifiers == KeyModifiers.None || ModifierKeys.Contains(key))
            {
                return key;
            }

            var text = string.Empty;

            foreach (var (modifier, name) in Order)
            {
                if ((modifiers & modifier) != 0)
                {
                    text += name + "+";
                }
            }

            return text + key;
        }
    }
}
