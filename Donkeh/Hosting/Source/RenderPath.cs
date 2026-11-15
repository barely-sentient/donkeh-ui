namespace Donkeh.Windowing;

/// <summary>
/// Selects how the OpenGL renderer submits its geometry to the GPU.
/// </summary>
/// <remarks>
/// <para>
/// The enum lives in <c>Donkeh.Hosting</c> so platform-neutral settings can
/// describe a render path without referencing the renderer implementation.
/// Browser hosts ignore it; native hosts forward it to their renderer.
/// </para>
/// <para>
/// <see cref="Indirect"/> is the GPU-driven path: all geometry for a frame is
/// baked into one GPU-resident instance buffer and submitted with indirect draw
/// commands, so the CPU does not walk the scene issuing a draw call per object.
/// <see cref="Direct"/> keeps the same geometry but submits one
/// <c>glDrawArraysInstanced</c> per batch; it exists as a reference path for
/// benchmarking and as a fallback on drivers without indirect-draw extensions.
/// </para>
/// </remarks>
public enum RenderPath
{
    /// <summary>Use indirect submission when the driver exposes the required extensions.</summary>
    Auto,

    /// <summary>Submit each batch with a direct instanced draw call.</summary>
    Direct,

    /// <summary>Submit batches through an indirect command buffer.</summary>
    Indirect
}
