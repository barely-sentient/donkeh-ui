using System.Numerics;

namespace Donkeh.LayoutLab;

/// <summary>The optimised instruction stream together with the pool it references.</summary>
/// <remarks>
/// When <see cref="LayoutOptions.CompactConstants"/> is enabled,
/// <see cref="Constants"/> is a newly allocated pool, not the input pool, and
/// the instructions in <see cref="Instructions"/> have been renumbered to match
/// it.
/// </remarks>
public sealed class OptimisationResult
{
    /// <summary>Creates a result from an optimised buffer.</summary>
    /// <param name="instructions">The output instruction buffer.</param>
    /// <param name="constants">The constant pool referenced by the output.</param>
    /// <param name="stats">The pass statistics.</param>
    public OptimisationResult(
        InstructionBuffer instructions,
        ConstantPool constants,
        OptimisationStats stats)
    {
        Instructions = instructions;
        Constants = constants;
        Stats = stats;
    }

    /// <summary>Gets the optimised display list.</summary>
    public InstructionBuffer Instructions { get; }

    /// <summary>Gets the constant pool referenced by <see cref="Instructions"/>.</summary>
    public ConstantPool Constants { get; }

    /// <summary>Gets the pass statistics.</summary>
    public OptimisationStats Stats { get; }
}

/// <summary>
/// A small instruction optimiser. It removes invisible work, folds adjacent
/// colour fills, collapses redundant clip/opacity scopes and optionally rebuilds
/// the constant pool so a frame only carries constants it references.
/// </summary>
/// <remarks>
/// <para>
/// The passes never change what the renderer draws: clip intersections only
/// remove geometry that is already outside the effective clip, merged rectangles
/// cover exactly the union of the originals, and redundant scopes are those
/// whose effect is the identity.
/// </para>
/// <para>
/// The methods are static and keep no state between calls, so concurrent runs
/// are safe as long as each call owns its input, output and constant pool.
/// Instructions are processed in paint order; draw culling is disabled inside
/// an active transform or filter scope, and rectangle merging is disabled
/// inside a transform scope, because those change the coordinate space or
/// sample the backdrop.
/// </para>
/// </remarks>
/// <seealso cref="InstructionBuffer"/>
/// <seealso cref="ConstantPool"/>
public static class Optimiser
{
    /// <summary>The kinds of push/pop scope tracked while walking the stream.</summary>
    private enum ScopeKind : byte
    {
        /// <summary>A rectangular or rounded clip push.</summary>
        Clip,

        /// <summary>A group-opacity push.</summary>
        Opacity,

        /// <summary>A transform push.</summary>
        Transform,

        /// <summary>A composited-layer push.</summary>
        Layer,

        /// <summary>A filter push.</summary>
        Filter,

        /// <summary>A backdrop-filter push.</summary>
        BackdropFilter,

        /// <summary>A clip-path push.</summary>
        ClipPath,

        /// <summary>A mask push.</summary>
        Mask,

        /// <summary>A blend-mode push.</summary>
        Blend
    }

    /// <summary>Bookkeeping for one open push/pop scope.</summary>
    /// <param name="Kind">The scope family, used to match pops.</param>
    /// <param name="Pop">The pop opcode that closes the scope; informational.</param>
    /// <param name="Emit">Whether the matching push and pop survive in the output.</param>
    /// <param name="PreviousClip">The clip in effect before a clip push.</param>
    /// <param name="ClipEmpty">Whether a clip push produced an empty intersection.</param>
    private readonly record struct Scope(
        ScopeKind Kind,
        InstructionOpCode Pop,
        bool Emit,
        Rect PreviousClip,
        bool ClipEmpty);

    /// <summary>Runs every enabled optimisation pass over a frame's instruction stream.</summary>
    /// <param name="source">The input display list; it is not modified.</param>
    /// <param name="constants">The pool referenced by <paramref name="source"/>.</param>
    /// <param name="viewport">The viewport rectangle in CSS pixels, used as the initial clip.</param>
    /// <param name="options">The flags controlling each pass.</param>
    /// <param name="outputBuffer">
    /// An optional buffer to clear and fill. It must not be the same instance as
    /// <paramref name="source"/>, because the output is cleared before the input
    /// is read.
    /// </param>
    /// <returns>The output buffer, the (possibly rebuilt) pool and the statistics.</returns>
    /// <remarks>
    /// <para>
    /// When <see cref="LayoutOptions.Optimise"/> is disabled the source buffer
    /// and pool are returned unchanged with <see cref="OptimisationStats.Empty"/>.
    /// </para>
    /// <para>
    /// Enabled passes, in order: <c>Nop</c> instructions are dropped; frame
    /// markers are dropped unless <see cref="LayoutOptions.IncludeFrameMarkers"/>
    /// is set; clip pushes are intersected with the current clip and dropped when
    /// they are empty or (for rectangular clips only) identical to the current
    /// clip; opacity pushes at or above <c>0.9999</c> are dropped with their
    /// pop; draws are culled when the current clip is empty, or when
    /// <see cref="LayoutOptions.CullOffscreen"/> is set, no transform/filter is
    /// active and the draw's bounds miss the clip; adjacent opaque
    /// <c>DrawRect</c> instructions are merged when
    /// <see cref="LayoutOptions.MergeDraws"/> is set and no clip changed between
    /// them; finally constants are compacted when
    /// <see cref="LayoutOptions.CompactConstants"/> is set.
    /// </para>
    /// <para>
    /// Rounded clips are never treated as redundant even when their bounds equal
    /// the current clip, because the corner rounding is part of their effect.
    /// Unmatched pops are ignored rather than clipping the rest of the frame.
    /// </para>
    /// </remarks>
    public static OptimisationResult Run(
        InstructionBuffer source,
        ConstantPool constants,
        Rect viewport,
        LayoutOptions options,
        InstructionBuffer? outputBuffer = null)
    {
        if (!options.Optimise)
        {
            return new OptimisationResult(source, constants, OptimisationStats.Empty);
        }

        var initialClip = options.ClipToViewport
            ? viewport
            : new Rect(-1e7f, -1e7f, 2e7f, 2e7f);

        // Capture the input before clearing: outputBuffer may alias source, and
        // clearing first would otherwise make the frame disappear. The captured
        // span is over the same backing array; because the output is pre-sized
        // to at least the source count it cannot grow while it is being read.
        var input = source.Span;

        var output = outputBuffer ?? new InstructionBuffer(Math.Max(8, source.Count));
        output.Clear();
        var scopes = new List<Scope>(8);
        var clip = initialClip;
        var transformDepth = 0;
        var filterDepth = 0;
        var clipVersion = 0;
        var lastClipVersion = -1;
        var culled = 0;
        var merged = 0;
        var scopesRemoved = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var instruction = input[i];

            switch (instruction.OpCode)
            {
                case InstructionOpCode.Nop:
                    continue;

                case InstructionOpCode.BeginFrame:
                case InstructionOpCode.EndFrame:
                    if (options.IncludeFrameMarkers)
                    {
                        output.Add(instruction);
                    }

                    continue;

                case InstructionOpCode.PushClipRect:
                case InstructionOpCode.PushClipRoundedRect:
                {
                    var previous = clip;
                    var next = previous.Intersect(instruction.Rect);
                    var empty = next.IsEmpty;

                    // A rounded clip cannot be dropped just because its bounds
                    // match the current clip: the corner rounding is part of the
                    // result. Keep it unless it is provably empty.
                    var redundant = instruction.OpCode == InstructionOpCode.PushClipRect
                        && !empty
                        && next.Equals(previous);

                    var emit = !empty && !redundant;

                    if (!emit)
                    {
                        scopesRemoved++;
                    }

                    scopes.Add(new Scope(ScopeKind.Clip, InstructionOpCode.PopClip, emit, previous, empty));
                    clip = empty ? Rect.Empty : next;
                    clipVersion++;

                    if (emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.PopClip:
                {
                    var scope = PopScope(scopes, ScopeKind.Clip, clip);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    clip = scope.PreviousClip;
                    clipVersion++;
                    continue;
                }

                case InstructionOpCode.PushOpacity:
                {
                    var visible = instruction.X < 0.9999f;

                    if (!visible)
                    {
                        scopesRemoved++;
                    }

                    scopes.Add(new Scope(ScopeKind.Opacity, InstructionOpCode.PopOpacity, visible, clip, false));

                    if (visible)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.PopOpacity:
                {
                    var scope = PopScope(scopes, ScopeKind.Opacity);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.PushTransform:
                    transformDepth++;
                    scopes.Add(new Scope(ScopeKind.Transform, InstructionOpCode.PopTransform, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopTransform:
                {
                    var scope = PopScope(scopes, ScopeKind.Transform);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    transformDepth = Math.Max(0, transformDepth - 1);
                    continue;
                }

                case InstructionOpCode.PushLayer:
                    scopes.Add(new Scope(ScopeKind.Layer, InstructionOpCode.PopLayer, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopLayer:
                {
                    var scope = PopScope(scopes, ScopeKind.Layer);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.SetScrollOffset:
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PushFilter:
                    filterDepth++;
                    scopes.Add(new Scope(ScopeKind.Filter, InstructionOpCode.PopFilter, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopFilter:
                {
                    var scope = PopScope(scopes, ScopeKind.Filter);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    filterDepth = Math.Max(0, filterDepth - 1);
                    continue;
                }

                case InstructionOpCode.PushBackdropFilter:
                    filterDepth++;
                    scopes.Add(new Scope(
                        ScopeKind.BackdropFilter,
                        InstructionOpCode.PopBackdropFilter,
                        true,
                        clip,
                        false));

                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopBackdropFilter:
                {
                    var scope = PopScope(scopes, ScopeKind.BackdropFilter);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    filterDepth = Math.Max(0, filterDepth - 1);
                    continue;
                }

                case InstructionOpCode.PushClipPath:
                    scopes.Add(new Scope(ScopeKind.ClipPath, InstructionOpCode.PopClipPath, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopClipPath:
                {
                    var scope = PopScope(scopes, ScopeKind.ClipPath);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.PushMask:
                    scopes.Add(new Scope(ScopeKind.Mask, InstructionOpCode.PopMask, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopMask:
                {
                    var scope = PopScope(scopes, ScopeKind.Mask);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }

                case InstructionOpCode.PushBlendMode:
                    scopes.Add(new Scope(ScopeKind.Blend, InstructionOpCode.PopBlendMode, true, clip, false));
                    output.Add(instruction);
                    continue;

                case InstructionOpCode.PopBlendMode:
                {
                    var scope = PopScope(scopes, ScopeKind.Blend);

                    if (scope.Emit)
                    {
                        output.Add(instruction);
                    }

                    continue;
                }
            }

            if (!instruction.IsDraw)
            {
                output.Add(instruction);
                continue;
            }

            // An empty clip is a hard guarantee that nothing inside can be seen,
            // even when general off-screen culling is turned off. Without this,
            // dropping the degenerate clip push would let its contents paint.
            if (clip.IsEmpty
                || (options.CullOffscreen
                    && transformDepth == 0
                    && filterDepth == 0
                    && !CullBounds(instruction, constants).Intersects(clip)))
            {
                culled++;
                continue;
            }

            if (options.MergeDraws
                && transformDepth == 0
                && instruction.OpCode == InstructionOpCode.DrawRect
                && TryMerge(output, instruction, clipVersion, ref lastClipVersion))
            {
                merged++;
                continue;
            }

            lastClipVersion = clipVersion;
            output.Add(instruction);
        }

        var finalConstants = constants;

        if (options.CompactConstants)
        {
            finalConstants = CompactConstants(output, constants);
        }

        var stats = new OptimisationStats(
            input.Length,
            output.Count,
            culled,
            merged,
            scopesRemoved);

        return new OptimisationResult(output, finalConstants, stats);
    }

    /// <summary>
    /// Bounds used for off-screen culling. Most draws are fully described by
    /// their rect; an outer shadow can bleed outside the border box.
    /// </summary>
    /// <param name="instruction">The draw instruction being tested.</param>
    /// <param name="constants">The pool used to expand a shadow constant.</param>
    /// <returns>The instruction rectangle, inflated for a shadow draw.</returns>
    /// <remarks>
    /// For <c>DrawShadow</c> the inflation is
    /// <c>|offsetX| + |offsetY| + blur + max(0, spread)</c>, an upper bound on
    /// the shadow's reach. The payload is range-checked against the pool; any
    /// other opcode returns its rectangle unchanged.
    /// </remarks>
    private static Rect CullBounds(in Instruction instruction, ConstantPool constants)
    {
        var rect = instruction.Rect;

        if (instruction.OpCode == InstructionOpCode.DrawShadow
            && (uint)instruction.Payload < (uint)constants.Count)
        {
            var shadow = constants.GetShadow(instruction.Payload);
            var reach = MathF.Abs(shadow.OffsetX)
                + MathF.Abs(shadow.OffsetY)
                + shadow.Blur
                + MathF.Max(0, shadow.Spread);

            return rect.Inflate(MathF.Max(0, reach));
        }

        return rect;
    }

    /// <summary>Pops the nearest scope of the expected kind, keeping the current clip.</summary>
    /// <param name="scopes">The open-scope stack.</param>
    /// <param name="expected">The scope kind the pop belongs to.</param>
    /// <returns>The removed scope, or a non-emitting placeholder when none matched.</returns>
    private static Scope PopScope(List<Scope> scopes, ScopeKind expected)
    {
        return PopScope(scopes, expected, Rect.Empty);
    }

    /// <summary>Pops the nearest scope of the expected kind.</summary>
    /// <param name="scopes">The open-scope stack.</param>
    /// <param name="expected">The scope kind the pop belongs to.</param>
    /// <param name="currentClip">The clip to preserve when the pop is unmatched.</param>
    /// <returns>The removed scope, or a non-emitting placeholder when none matched.</returns>
    /// <remarks>
    /// Searches from the top of the stack so interleaved scopes of other kinds
    /// are tolerated. An unmatched pop is a no-op: keeping the current clip
    /// avoids turning a malformed stream into a fully clipped (blank) frame.
    /// </remarks>
    private static Scope PopScope(List<Scope> scopes, ScopeKind expected, in Rect currentClip)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            if (scopes[i].Kind == expected)
            {
                var scope = scopes[i];
                scopes.RemoveAt(i);
                return scope;
            }
        }

        // An unmatched pop is a no-op: keeping the current clip avoids turning a
        // malformed stream into a fully clipped (blank) frame.
        return new Scope(expected, InstructionOpCode.Nop, false, currentClip, false);
    }

    /// <summary>Attempts to fuse a rectangle into the previous output rectangle.</summary>
    /// <param name="output">The buffer written so far; the last instruction may be replaced.</param>
    /// <param name="instruction">The incoming <c>DrawRect</c>.</param>
    /// <param name="clipVersion">The current clip generation.</param>
    /// <param name="lastClipVersion">The clip generation at the last emitted draw.</param>
    /// <returns><see langword="true"/> when the incoming rect was merged and must not be emitted.</returns>
    /// <remarks>
    /// Merging is refused when the clip changed since the previous draw, when the
    /// previous instruction is not a <c>DrawRect</c>, when payload or flags
    /// differ, or when the fill is not fully opaque (merging translucent fills
    /// would lose a compositing layer). Two rectangles merge when they share a
    /// row (same y and height) or a column (same x and width) within 0.01 px and
    /// touch or overlap along the other axis.
    /// </remarks>
    private static bool TryMerge(
        InstructionBuffer output,
        in Instruction instruction,
        int clipVersion,
        ref int lastClipVersion)
    {
        if (clipVersion != lastClipVersion || output.Count == 0)
        {
            return false;
        }

        var previous = output[output.Count - 1];

        if (previous.OpCode != InstructionOpCode.DrawRect
            || previous.Payload != instruction.Payload
            || previous.Flags != instruction.Flags)
        {
            return false;
        }

        // A merged rect paints once. Overlapping translucent fills would lose a
        // layer of compositing, so only opaque colour fills are safe to merge.
        if (((uint)previous.Payload >> 24) != 0xFF)
        {
            return false;
        }

        var a = previous.Rect;
        var b = instruction.Rect;
        const float epsilon = 0.01f;

        if (MathF.Abs(a.Y - b.Y) <= epsilon
            && MathF.Abs(a.Height - b.Height) <= epsilon
            && b.X <= a.Right + epsilon
            && b.Right >= a.X - epsilon)
        {
            var x = MathF.Min(a.X, b.X);
            var right = MathF.Max(a.Right, b.Right);
            output[output.Count - 1] = Instruction.DrawRect(new Rect(x, a.Y, right - x, a.Height), (uint)previous.Payload, previous.Flags);
            return true;
        }

        if (MathF.Abs(a.X - b.X) <= epsilon
            && MathF.Abs(a.Width - b.Width) <= epsilon
            && b.Y <= a.Bottom + epsilon
            && b.Bottom >= a.Y - epsilon)
        {
            var y = MathF.Min(a.Y, b.Y);
            var bottom = MathF.Max(a.Bottom, b.Bottom);
            output[output.Count - 1] = Instruction.DrawRect(new Rect(a.X, y, a.Width, bottom - y), (uint)previous.Payload, previous.Flags);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the pool in first-use order. Instructions are rewritten in place
    /// so the caller gets a smaller, tighter frame with the same visual output.
    /// </summary>
    /// <param name="buffer">The optimised instructions; payloads are rewritten in place.</param>
    /// <param name="source">The pool referenced by the original payloads.</param>
    /// <returns>A new pool containing only constants reachable from <paramref name="buffer"/>.</returns>
    /// <remarks>
    /// <para>
    /// Each constant kind keeps its own old-to-new id map, so identical values
    /// used by different kinds are never conflated. The first reference to a
    /// value defines its new id; later references reuse it. Constants that the
    /// optimised stream no longer references are dropped.
    /// </para>
    /// <para>
    /// Corner radii are addressed through <see cref="Instruction.Aux"/> rather
    /// than the payload and are renumbered with a checked 16-bit cast. The input
    /// pool is left untouched, so callers must use the returned pool from then
    /// on.
    /// </para>
    /// </remarks>
    private static ConstantPool CompactConstants(InstructionBuffer buffer, ConstantPool source)
    {
        var pool = new ConstantPool();
        var borders = new Dictionary<int, int>();
        var shadows = new Dictionary<int, int>();
        var backgrounds = new Dictionary<int, int>();
        var images = new Dictionary<int, int>();
        var scrollbars = new Dictionary<int, int>();
        var texts = new Dictionary<int, int>();
        var transforms = new Dictionary<int, int>();
        var radii = new Dictionary<int, int>();
        var filters = new Dictionary<int, int>();
        var clipPaths = new Dictionary<int, int>();
        var masks = new Dictionary<int, int>();

        for (var i = 0; i < buffer.Count; i++)
        {
            var instruction = buffer.Array[i];
            var payload = instruction.Payload;
            int mapped;

            switch (instruction.OpCode)
            {
                case InstructionOpCode.DrawBorder:
                case InstructionOpCode.DrawOutline:
                    if (!borders.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddBorder(source.GetBorder(payload));
                        borders[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawShadow:
                case InstructionOpCode.DrawInsetShadow:
                    if (!shadows.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddShadow(source.GetShadow(payload));
                        shadows[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawBackgroundImage:
                    if (!backgrounds.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddBackground(source.GetBackground(payload));
                        backgrounds[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawImage:
                    if (!images.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddImage(source.GetImage(payload));
                        images[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawScrollbar:
                    if (!scrollbars.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddScrollbar(source.GetScrollbar(payload));
                        scrollbars[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawText:
                    if (!texts.TryGetValue(payload, out mapped))
                    {
                        var text = source.GetText(payload);
                        mapped = pool.AddText(text.Text, text.Style);
                        texts[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.PushTransform:
                    if (!transforms.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddTransform(source.GetTransform(payload));
                        transforms[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.PushFilter:
                case InstructionOpCode.PushBackdropFilter:
                    if (!filters.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddFilter(source.GetFilter(payload));
                        filters[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.PushClipPath:
                    if (!clipPaths.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddClipPath(source.GetClipPath(payload));
                        clipPaths[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.PushMask:
                    if (!masks.TryGetValue(payload, out mapped))
                    {
                        mapped = pool.AddMask(source.GetMask(payload));
                        masks[payload] = mapped;
                    }

                    buffer[i] = instruction.WithPayload(mapped);
                    break;

                case InstructionOpCode.DrawRoundedRect:
                case InstructionOpCode.PushClipRoundedRect:
                {
                    var old = instruction.Aux;

                    if (!radii.TryGetValue(old, out mapped))
                    {
                        mapped = pool.AddRadius(source.GetRadius(old));
                        radii[old] = mapped;
                    }

                    buffer[i] = instruction.WithAux(checked((ushort)mapped));
                    break;
                }
            }
        }

        return pool;
    }
}
