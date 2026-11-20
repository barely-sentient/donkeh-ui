using System.Runtime.InteropServices;
using Donkeh.LayoutLab;
using Donkeh.UILab;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DonkehFrameworkTests")]

namespace Donkeh.Windowing;

/// <summary>
/// Shows the platform colour chooser for an <c>input[type=color]</c>.
/// </summary>
/// <remarks>
/// Desktop hosts use the native Windows common colour dialog. Other platforms
/// (and hosts without a window handle) return <see langword="false"/> so the
/// caller can leave the control's value untouched. The dialog is modal, so the
/// frame loop naturally pauses while it is open.
/// </remarks>
internal static class ColorDialog
{
    private const uint CcRgbInit = 0x00000001;
    private const uint CcFullOpen = 0x00000002;
    private const uint CcAnyColor = 0x00000100;

    /// <summary>Converts a CSS colour to the Win32 <c>COLORREF</c> form.</summary>
    /// <param name="value">A CSS colour such as <c>#0b57d0</c>, or <see langword="null"/>.</param>
    /// <returns>Packed <c>0x00BBGGRR</c>, or zero when the value cannot be parsed.</returns>
    public static uint ToColorRef(string? value) =>
        CssColor.TryParse(value, CssColor.Black, out var argb)
            ? ((argb & 0xFF) << 16) | (argb & 0x00FF00) | ((argb >> 16) & 0xFF)
            : 0;

    /// <summary>Formats a Win32 <c>COLORREF</c> as a lower-case CSS hex colour.</summary>
    /// <param name="colorRef">Packed <c>0x00BBGGRR</c>.</param>
    /// <returns>A value such as <c>#0b57d0</c>.</returns>
    public static string ToCssHex(uint colorRef) =>
        $"#{(colorRef & 0xFF):x2}{((colorRef >> 8) & 0xFF):x2}{((colorRef >> 16) & 0xFF):x2}";

    /// <summary>Stores a selected colour on a colour input.</summary>
    /// <param name="element">The <c>input[type=color]</c> receiving the value.</param>
    /// <param name="colorRef">Selected colour in Win32 <c>COLORREF</c> form.</param>
    /// <returns>The CSS hex string written to the element.</returns>
    public static string Apply(ElementNode element, uint colorRef)
    {
        var hex = ToCssHex(colorRef);
        element.SetAttribute("value", hex);
        return hex;
    }

    /// <summary>Shows the colour chooser and reports the selected colour.</summary>
    /// <param name="owner">Native owner window handle, or zero for no owner.</param>
    /// <param name="initialRgb">Initial colour in Win32 <c>COLORREF</c> form (0x00BBGGRR).</param>
    /// <param name="customColors">Sixteen persistent custom colour slots.</param>
    /// <param name="selectedRgb">Receives the selected colour on success.</param>
    /// <returns><see langword="true"/> when the user accepted a colour.</returns>
    public static bool TryPick(
        IntPtr owner,
        uint initialRgb,
        uint[] customColors,
        out uint selectedRgb)
    {
        selectedRgb = initialRgb;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var pinned = GCHandle.Alloc(customColors, GCHandleType.Pinned);

        try
        {
            var data = new ChooseColorData
            {
                Size = (uint)Marshal.SizeOf<ChooseColorData>(),
                Owner = owner,
                Result = initialRgb,
                CustomColors = pinned.AddrOfPinnedObject(),
                Flags = CcRgbInit | CcFullOpen | CcAnyColor
            };

            if (!ChooseColor(ref data))
            {
                return false;
            }

            selectedRgb = data.Result;
            return true;
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Win32 <c>CHOOSECOLORW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public uint Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public uint Result;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport(
        "comdlg32.dll",
        EntryPoint = "ChooseColorW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorData data);
}
