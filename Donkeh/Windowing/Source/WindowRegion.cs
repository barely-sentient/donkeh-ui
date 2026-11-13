using System.Runtime.InteropServices;
using Donkeh.LayoutLab;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Applies a rounded shape to a native Win32 window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renderer already masks the framebuffer to the same rounded outline, and
    /// that alpha mask is what gives frameless windows smooth corners on
    /// compositors that honour a transparent framebuffer. This helper additionally
    /// sets the platform window region, so the shape also applies to native
    /// decorations and to systems where per-pixel framebuffer transparency is not
    /// available.
    /// </para>
    /// <para>
    /// The region is built in physical pixels from the visible window bounds, so it
    /// follows the same radius on high-DPI displays. Only Win32 is supported; every
    /// method is a no-op on other platforms. The class keeps no state, and region
    /// handles are owned by the system after a successful
    /// <c>SetWindowRgn</c> call.
    /// </para>
    /// </remarks>
    /// <seealso cref="IgniteWindow"/>
    internal static class WindowRegion
    {
        /// <summary>Win32 window rectangle, in physical screen pixels.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>Win32 point used by <c>CreatePolygonRgn</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>, the visible frame bounds excluding
        /// the invisible resize margin.
        /// </summary>
        private const int ExtendedFrameBounds = 9;

        /// <summary>Fill mode passed to <c>CreatePolygonRgn</c>.</summary>
        private const int WindingFill = 2;

        /// <summary>Approximation segments used for each corner arc.</summary>
        private const int ArcSegments = 12;

        /// <summary>Returns the window rectangle including non-client decorations.</summary>
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hwnd, out Rect rect);

        /// <summary>Returns the visible frame bounds reported by the compositor.</summary>
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);

        /// <summary>Creates a polygonal region from the supplied points.</summary>
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreatePolygonRgn(Point[] points, int count, int fillMode);

        /// <summary>Assigns a region to a window, taking ownership on success.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

        /// <summary>Deletes a GDI object that was not handed to the system.</summary>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(nint obj);

        /// <summary>
        /// Applies a rounded window region, or clears it when the radii are zero.
        /// </summary>
        /// <param name="hwnd">The native window handle.</param>
        /// <param name="corners">Corner radii in CSS pixels.</param>
        /// <param name="dpi">Device-to-CSS-pixel ratio used to scale the radii.</param>
        /// <returns>
        /// <see langword="true"/> when a region was applied or cleared;
        /// <see langword="false"/> on unsupported platforms or when the call
        /// failed.
        /// </returns>
        /// <remarks>
        /// A zero radius clears any region previously set through this helper. This
        /// restores the platform's default window shape instead of leaving a stale
        /// clip behind when a window becomes square again.
        /// </remarks>
        public static bool Apply(nint hwnd, CornerRadius corners, float dpi)
        {
            if (!OperatingSystem.IsWindows() || hwnd == 0)
            {
                return false;
            }

            var width = 0;
            var height = 0;
            var offsetX = 0;
            var offsetY = 0;

            if (!GetWindowRect(hwnd, out var windowRect))
            {
                return false;
            }

            // Prefer the compositor's visible bounds so a decorated window's
            // invisible resize margin does not consume the rounded edge.
            var visibleRect = windowRect;

            if (DwmGetWindowAttribute(
                    hwnd,
                    ExtendedFrameBounds,
                    out var dwmRect,
                    Marshal.SizeOf<Rect>()) == 0
                && IsUsable(dwmRect))
            {
                visibleRect = dwmRect;
            }

            width = visibleRect.Right - visibleRect.Left;
            height = visibleRect.Bottom - visibleRect.Top;
            offsetX = visibleRect.Left - windowRect.Left;
            offsetY = visibleRect.Top - windowRect.Top;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (corners.IsZero)
            {
                SetWindowRgn(hwnd, 0, true);
                return true;
            }

            var scale = dpi > 0 ? dpi : 1;
            var maxRadiusX = width * 0.5f;
            var maxRadiusY = height * 0.5f;

            var topLeft = Clamp(corners.TopLeft * scale, maxRadiusX, maxRadiusY);
            var topRight = Clamp(corners.TopRight * scale, maxRadiusX, maxRadiusY);
            var bottomRight = Clamp(corners.BottomRight * scale, maxRadiusX, maxRadiusY);
            var bottomLeft = Clamp(corners.BottomLeft * scale, maxRadiusX, maxRadiusY);

            var points = BuildRoundedRect(
                offsetX,
                offsetY,
                width,
                height,
                topLeft,
                topRight,
                bottomRight,
                bottomLeft);

            var region = CreatePolygonRgn(points, points.Length, WindingFill);

            if (region == 0)
            {
                return false;
            }

            if (SetWindowRgn(hwnd, region, true) == 0)
            {
                DeleteObject(region);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Runs one axis of a pair of radii through a shared clamp.
        /// </summary>
        /// <param name="radius">The scaled radius, in device pixels.</param>
        /// <param name="maxX">Half the visible width in device pixels.</param>
        /// <param name="maxY">Half the visible height in device pixels.</param>
        /// <returns>The clamped, rounded radius.</returns>
        private static float Clamp(float radius, float maxX, float maxY) =>
            MathF.Round(Math.Clamp(radius, 0, MathF.Min(maxX, maxY)));

        /// <summary>
        /// Builds a clockwise polygon for a rectangle with independent corner
        /// radii.
        /// </summary>
        /// <param name="x">Left edge in device pixels.</param>
        /// <param name="y">Top edge in device pixels.</param>
        /// <param name="width">Visible width in device pixels.</param>
        /// <param name="height">Visible height in device pixels.</param>
        /// <param name="topLeft">Top-left radius in device pixels.</param>
        /// <param name="topRight">Top-right radius in device pixels.</param>
        /// <param name="bottomRight">Bottom-right radius in device pixels.</param>
        /// <param name="bottomLeft">Bottom-left radius in device pixels.</param>
        /// <returns>The polygon vertices in window coordinates.</returns>
        /// <remarks>
        /// Each corner contributes one arc sampled at
        /// <see cref="ArcSegments"/> intervals. Zero-radius corners still emit their
        /// shared edge endpoints, so the resulting shape remains a valid polygon.
        /// </remarks>
        private static Point[] BuildRoundedRect(
            int x,
            int y,
            int width,
            int height,
            float topLeft,
            float topRight,
            float bottomRight,
            float bottomLeft)
        {
            var points = new List<Point>(4 * (ArcSegments + 1));
            var right = x + width;
            var bottom = y + height;

            points.Add(new Point { X = (int)MathF.Round(x + topLeft), Y = y });

            if (topRight > 0)
            {
                AddArc(points, right - topRight, y + topRight, topRight, -90, 0);
            }
            else
            {
                points.Add(new Point { X = right, Y = y });
            }

            if (bottomRight > 0)
            {
                AddArc(points, right - bottomRight, bottom - bottomRight, bottomRight, 0, 90);
            }
            else
            {
                points.Add(new Point { X = right, Y = bottom });
            }

            if (bottomLeft > 0)
            {
                AddArc(points, x + bottomLeft, bottom - bottomLeft, bottomLeft, 90, 180);
            }
            else
            {
                points.Add(new Point { X = x, Y = bottom });
            }

            if (topLeft > 0)
            {
                AddArc(points, x + topLeft, y + topLeft, topLeft, 180, 270);
            }
            else
            {
                points.Add(new Point { X = x, Y = y });
            }

            return points.ToArray();
        }

        /// <summary>
        /// Appends arc samples for one rounded corner.
        /// </summary>
        /// <param name="points">Destination vertex list.</param>
        /// <param name="centerX">Arc centre X in device pixels.</param>
        /// <param name="centerY">Arc centre Y in device pixels.</param>
        /// <param name="radius">Arc radius in device pixels.</param>
        /// <param name="startDegrees">First angle in degrees, measured clockwise.</param>
        /// <param name="endDegrees">Last angle in degrees, measured clockwise.</param>
        private static void AddArc(
            List<Point> points,
            float centerX,
            float centerY,
            float radius,
            float startDegrees,
            float endDegrees)
        {
            for (var i = 0; i <= ArcSegments; i++)
            {
                var angle = (startDegrees + (endDegrees - startDegrees) * i / ArcSegments)
                    * MathF.PI / 180f;
                points.Add(new Point
                {
                    X = (int)MathF.Round(centerX + MathF.Cos(angle) * radius),
                    Y = (int)MathF.Round(centerY + MathF.Sin(angle) * radius)
                });
            }
        }

        /// <summary>Checks that a compositor rectangle has a positive area.</summary>
        /// <param name="rect">The rectangle to validate.</param>
        /// <returns><see langword="true"/> for a non-degenerate rectangle.</returns>
        private static bool IsUsable(Rect rect) =>
            rect.Right > rect.Left && rect.Bottom > rect.Top;
    }
}
