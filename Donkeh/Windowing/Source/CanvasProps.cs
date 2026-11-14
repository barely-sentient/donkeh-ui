using Donkeh.UILab;
using Silk.NET.OpenGL;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Props for a <c>canvas</c> element. Extends <see cref="Props"/> with a
    /// callback that receives a fresh Silk.NET OpenGL context scoped to the
    /// element's bounds.
    /// </summary>
    public sealed class CanvasProps : Props
    {
        /// <summary>
        /// Handler invoked every frame for this canvas. The argument is a new
        /// Silk.NET OpenGL instance bound to the host window's context; the
        /// host has already set the viewport and scissor to the element's
        /// bounds, so drawing happens inside the canvas.
        /// </summary>
        /// <remarks>
        /// This is the immediate-mode escape hatch: commands run after the
        /// frame's indirect batch has been submitted, so they paint on top and
        /// are not batched. Prefer <see cref="OnDraw"/> for ordinary 2D content.
        /// </remarks>
        public Action<GL>? OnContext
        {
            get => OnCanvasContext as Action<GL>;
            init => OnCanvasContext = value;
        }

        /// <summary>
        /// Handler invoked every frame to record canvas primitives into the
        /// frame's indirect render batch.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The callback receives a <see cref="CanvasContext"/> whose coordinates
        /// are canvas-local CSS pixels. Rectangles, rounded rectangles, ellipses,
        /// text, images, clips, transforms and opacity are appended to the same
        /// GPU instance buffer as the rest of the UI, so a canvas does not cost a
        /// viewport switch, a separate buffer upload or a draw call per shape.
        /// </para>
        /// <para>
        /// Both callbacks may be set. Batched <see cref="OnDraw"/> content paints
        /// first, then raw <see cref="OnContext"/> commands paint over it.
        /// </para>
        /// </remarks>
        public Action<CanvasContext>? OnDraw
        {
            get => OnCanvasDraw as Action<CanvasContext>;
            init => OnCanvasDraw = value;
        }
    }
}
