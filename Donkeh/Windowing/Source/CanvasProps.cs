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
        public Action<GL>? OnContext
        {
            get => OnCanvasContext as Action<GL>;
            init => OnCanvasContext = value;
        }
    }
}