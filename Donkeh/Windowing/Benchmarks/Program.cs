using System.Diagnostics;
using System.Numerics;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Donkeh.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

// A small headless harness for the window renderer. It builds synthetic
// instruction streams, submits the same stream through the direct and indirect
// paths, and prints CPU submission and GPU completion timings. The window is
// hidden, so the harness can run on a build agent with a GPU but no desktop.

var options = WindowOptions.Default with
{
    IsVisible = false,
    Size = new Silk.NET.Maths.Vector2D<int>(1280, 800),
    API = new GraphicsAPI(
        ContextAPI.OpenGL,
        ContextProfile.Core,
        ContextFlags.ForwardCompatible,
        new APIVersion(3, 3)),
    PreferredDepthBufferBits = 24,
    PreferredStencilBufferBits = 8
};

var window = Window.Create(options);

window.Load += () =>
{
    unsafe
    {
        var gl = GL.GetApi(window);
        using var fonts = new FontManager();
        fonts.Initialize(gl);
        using var images = new ImageCache();
        images.Initialize(gl);
        using var renderer = new GLRenderer(gl, fonts, images);

        Console.WriteLine($"GL       : {gl.GetStringS(StringName.Version)} / {gl.GetStringS(StringName.Renderer)}");
        Console.WriteLine($"Path     : {renderer.EffectivePath} (requested {renderer.Path})");
        Console.WriteLine();

        var scalingCount = ReadInt("--count");
        var iterations = ReadInt("--iterations") ?? 60;

        var scenarios = new[]
        {
            new Scenario("rects", scalingCount ?? 10000, MakeRects),
            new Scenario("clipped", scalingCount ?? 10000, MakeClipped),
            new Scenario("transformed", scalingCount ?? 10000, MakeTransformed),
            new Scenario("text", scalingCount ?? 4000, MakeText),
            new Scenario("mixed", scalingCount ?? 4000, MakeMixed)
        };

        foreach (var scenario in scenarios)
        {
            var result = Build(scenario.Count, scenario.Builder);

            Console.WriteLine($"{scenario.Name} ({scenario.Count:N0} items)");
            Console.WriteLine("  path      build ms   submit ms   gpu ms   draws   commands   instances");
            Console.WriteLine("  -------------------------------------------------------------------------");

            foreach (var path in new[] { RenderPath.Indirect, RenderPath.Direct })
            {
                renderer.Path = path;
                var (build, submit, gpu) = Measure(gl, renderer, result, iterations);
                Console.WriteLine(
                    $"  {path,-9} {build,8:F3} {submit,11:F3} {gpu,8:F3} {renderer.LastDrawCalls,7} {renderer.LastCommandCount,10} {renderer.LastInstanceCount,11}");
            }

            var error = gl.GetError();

            if (error != GLEnum.NoError)
            {
                Console.WriteLine($"  GL error: {error}");
            }

            Console.WriteLine();
        }

        renderer.Path = RenderPath.Auto;
        window.Close();
    }
};

window.Run();

static (double Build, double Submit, double Gpu) Measure(
    GL gl,
    GLRenderer renderer,
    LayoutResult result,
    int iterations)
{
    var warmup = Math.Max(10, iterations / 6);

    for (var i = 0; i < warmup; i++)
    {
        renderer.BeginFrame(1280, 800, 1280, 800, 1, new Vector4(1, 1, 1, 1));
        renderer.Submit(result);
        renderer.Flush();
    }

    gl.Finish();
    var build = 0.0;
    var submit = 0.0;

    for (var i = 0; i < iterations; i++)
    {
        renderer.BeginFrame(1280, 800, 1280, 800, 1, new Vector4(1, 1, 1, 1));

        var start = Stopwatch.GetTimestamp();
        renderer.Submit(result);
        build += Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        start = Stopwatch.GetTimestamp();
        renderer.Flush();
        submit += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    gl.Finish();
    var gpuStart = Stopwatch.GetTimestamp();

    for (var i = 0; i < iterations; i++)
    {
        renderer.BeginFrame(1280, 800, 1280, 800, 1, new Vector4(1, 1, 1, 1));
        renderer.Submit(result);
        renderer.Flush();
        gl.Finish();
    }

    var gpu = Stopwatch.GetElapsedTime(gpuStart).TotalMilliseconds;

    return (build / iterations, submit / iterations, gpu / iterations);
}

static int? ReadInt(string name)
{
    var args = Environment.GetCommandLineArgs();

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i + 1], out var value))
        {
            return value;
        }
    }

    return null;
}

static LayoutResult Build(int count, Action<InstructionBuffer, ConstantPool, int> builder)
{
    var instructions = new InstructionBuffer(count * 3);
    var constants = new ConstantPool();
    builder(instructions, constants, count);

    return new LayoutResult(
        null,
        instructions,
        constants,
        new Dictionary<IgniteSupportedDomNode, LayoutBox>(),
        new OptimisationStats(0, 0, 0, 0, 0),
        snapshot: false);
}

static void MakeRects(InstructionBuffer instructions, ConstantPool constants, int count)
{
    for (var i = 0; i < count; i++)
    {
        instructions.AddDrawRect(Cell(i), 0xFF2563EB);
    }
}

static void MakeClipped(InstructionBuffer instructions, ConstantPool constants, int count)
{
    for (var i = 0; i < count; i++)
    {
        var rect = Cell(i);
        instructions.AddPushClip(rect.Inflate(2));
        instructions.AddDrawRect(rect, 0xFF2563EB);
        instructions.AddPopClip();
    }
}

static void MakeTransformed(InstructionBuffer instructions, ConstantPool constants, int count)
{
    for (var i = 0; i < count; i++)
    {
        var matrix = constants.AddTransform(Matrix3x2.CreateTranslation((i % 7) * 0.5f, (i % 5) * 0.25f));
        instructions.AddPushTransform(matrix);
        instructions.AddDrawRect(Cell(i), 0xFF2563EB);
        instructions.AddPopTransform();
    }
}

static void MakeText(InstructionBuffer instructions, ConstantPool constants, int count)
{
    var text = constants.AddText("Indirect glyph run", TextStyle.Default);

    for (var i = 0; i < count; i++)
    {
        var rect = Cell(i);
        instructions.Add(new Instruction(
            InstructionOpCode.DrawText,
            rect.X,
            rect.Y,
            rect.Width,
            20,
            text));
    }
}

static void MakeMixed(InstructionBuffer instructions, ConstantPool constants, int count)
{
    var radius = constants.AddRadius(CornerRadius.All(6));
    var text = constants.AddText("Mixed content", TextStyle.Default);

    for (var i = 0; i < count; i++)
    {
        var rect = Cell(i);
        instructions.AddDrawRoundedRect(rect, 0xFF2563EB, (ushort)radius);
        instructions.Add(new Instruction(
            InstructionOpCode.DrawText,
            rect.X + 4,
            rect.Y + 2,
            rect.Width - 8,
            16,
            text));
        instructions.AddDrawRect(
            new Rect(rect.X, rect.Bottom - 1, rect.Width, 1),
            0x33000000);
    }
}

static Rect Cell(int index)
{
    const int columns = 100;
    var x = index % columns * 12 + 4;
    var y = index / columns % 60 * 12 + 4;
    return new Rect(x, y, 10, 8);
}

/// <summary>One synthetic workload.</summary>
internal sealed record Scenario(
    string Name,
    int Count,
    Action<InstructionBuffer, ConstantPool, int> Builder);
