using System.Numerics;

namespace Ignite.LayoutLab;

public sealed class OptimisationResult
{
    public OptimisationResult(
        InstructionBuffer instructions,
        ConstantPool constants,
        OptimisationStats stats)
    {
        Instructions = instructions;
        Constants = constants;
        Stats = stats;
    }

    public InstructionBuffer Instructions { get; }
    public ConstantPool Constants { get; }
    public OptimisationStats Stats { get; }
}

/// <summary>
/// A small instruction optimiser. It removes invisible work, folds adjacent
/// colour fills, collapses redundant clip/opacity scopes and optionally rebuilds
/// the constant pool so a frame only carries constants it references.
/// </summary>
/// <remarks>
/// The passes never change what the renderer draws: clip intersections only
/// remove geometry that is already outside the effective clip, merged rectangles
/// cover exactly the union of the originals, and redundant scopes are those
/// whose effect is the identity.
/// </remarks>
public static class Optimiser
{
    private enum ScopeKind : byte
    {
        Clip,
        Opacity,
        Transform,
        Layer,
        Filter,
        BackdropFilter,
        ClipPath,
        Mask,
        Blend
    }

    private readonly record struct Scope(
        ScopeKind Kind,
        InstructionOpCode Pop,
        bool Emit,
        Rect PreviousClip,
        bool ClipEmpty);

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

        var input = source.Span;

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
            source.Count,
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

    private static Scope PopScope(List<Scope> scopes, ScopeKind expected)
    {
        return PopScope(scopes, expected, Rect.Empty);
    }

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
