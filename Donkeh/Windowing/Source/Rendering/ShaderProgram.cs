using Silk.NET.OpenGL;

namespace Donkeh.Windowing;

/// <summary>
/// A compiled and linked OpenGL shader program with a memoised map of uniform
/// names to locations.
/// </summary>
/// <remarks>
/// <para>
/// The constructor compiles the two stages, links them, then detaches and
/// deletes the stage objects, so the program is the only GL object kept alive.
/// Uniform locations are resolved lazily on first use and cached, including
/// <c>-1</c> for names the linker removed, making repeated setters cheap.
/// </para>
/// <para>
/// This type owns one GL program and nothing else; <see cref="Dispose"/> deletes
/// it exactly once. All members require the GL context to be current on the
/// calling thread, and uniform setters require <see cref="Use"/> to have been
/// called first because they act on the currently bound program.
/// </para>
/// </remarks>
internal sealed class ShaderProgram : IDisposable
{
    /// <summary>The OpenGL entry point for the context owning the program.</summary>
    private readonly GL _gl;

    /// <summary>The linked GL program name; deleted by <see cref="Dispose"/>.</summary>
    private readonly uint _program;

    /// <summary>
    /// Memoises <c>glGetUniformLocation</c> results per name, including misses
    /// stored as <c>-1</c>. Lookup is ordinal and case-sensitive, as GL requires.
    /// </summary>
    private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);

    /// <summary>
    /// Compiles both stages, links them into a program and deletes the stage
    /// objects.
    /// </summary>
    /// <param name="gl">The Silk.NET OpenGL entry point for the current context.</param>
    /// <param name="vertexSource">
    /// Complete GLSL vertex shader source, including the version directive.
    /// </param>
    /// <param name="fragmentSource">
    /// Complete GLSL fragment shader source, including the version directive.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either stage fails to compile, or when linking fails. The
    /// message includes the GL shader or program info log.
    /// </exception>
    /// <remarks>
    /// Compilation and linking are synchronous and comparatively expensive, so
    /// build programs once at start-up. On failure the exception propagates
    /// before the stage objects are detached, and the partially created shader
    /// and program names are not deleted by this constructor.
    /// </remarks>
    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        _program = gl.CreateProgram();
        gl.AttachShader(_program, vertex);
        gl.AttachShader(_program, fragment);
        gl.LinkProgram(_program);
        gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);

        if (linked == 0)
        {
            throw new InvalidOperationException("Shader link failed: " + gl.GetProgramInfoLog(_program));
        }

        gl.DetachShader(_program, vertex);
        gl.DetachShader(_program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
    }

    /// <summary>
    /// Gets the raw GL program name, for binding or for GL calls not wrapped
    /// here.
    /// </summary>
    /// <value>A non-zero program name that stays valid until <see cref="Dispose"/>.</value>
    public uint Handle => _program;

    /// <summary>Binds this program as the current program with <c>glUseProgram</c>.</summary>
    /// <remarks>
    /// The binding is context state and persists until another program is used,
    /// so uniform setters must not be called after an unrelated program has been
    /// bound. This call does not validate that the program still exists.
    /// </remarks>
    public void Use() => _gl.UseProgram(_program);

    /// <summary>Gets the cached location of a uniform, querying GL on first use.</summary>
    /// <param name="name">The exact uniform name as declared in GLSL.</param>
    /// <returns>
    /// The location, or <c>-1</c> when the uniform does not exist or was optimised
    /// out. A miss is cached, so later calls do not query GL again.
    /// </returns>
    /// <remarks>
    /// Querying does not require the program to be in use, only to exist. A
    /// <c>-1</c> result is harmless for the setters below: GL ignores uniform
    /// calls with location <c>-1</c>.
    /// </remarks>
    public int Location(string name)
    {
        if (_uniforms.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var location = _gl.GetUniformLocation(_program, name);
        _uniforms[name] = location;
        return location;
    }

    /// <summary>Sets an integer or sampler uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The integer value or texture unit index.</param>
    /// <remarks>
    /// Requires <see cref="Use"/> first; silently does nothing if the uniform is
    /// absent.
    /// </remarks>
    public void Uniform1(string name, int value) => _gl.Uniform1(Location(name), value);

    /// <summary>Sets a two-component floating-point uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <remarks>
    /// Requires <see cref="Use"/> first; silently does nothing if the uniform is
    /// absent.
    /// </remarks>
    public void Uniform2(string name, float x, float y) => _gl.Uniform2(Location(name), x, y);

    /// <summary>Sets a three-component floating-point uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <remarks>
    /// Requires <see cref="Use"/> first; silently does nothing if the uniform is
    /// absent.
    /// </remarks>
    public void Uniform3(string name, float x, float y, float z) =>
        _gl.Uniform3(Location(name), x, y, z);

    /// <summary>
    /// Sets a four-component floating-point uniform, used for rectangles and
    /// colours.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <param name="w">Fourth component.</param>
    /// <remarks>
    /// Requires <see cref="Use"/> first; silently does nothing if the uniform is
    /// absent.
    /// </remarks>
    public void Uniform4(string name, float x, float y, float z, float w) =>
        _gl.Uniform4(Location(name), x, y, z, w);

    /// <summary>Compiles one shader stage, throwing with the GL info log on failure.</summary>
    /// <param name="type">The stage to compile, vertex or fragment.</param>
    /// <param name="source">Complete GLSL source for the stage.</param>
    /// <returns>The GL shader name, which the caller must attach and later delete.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>GL_COMPILE_STATUS</c> is false; the message contains the
    /// stage type and the shader info log.
    /// </exception>
    /// <remarks>
    /// A failed compile leaves the shader object allocated, because the throw
    /// happens before it can be deleted; the program is torn down with the
    /// context in practice, but a renderer that retries compilation leaks one
    /// shader object per attempt.
    /// </remarks>
    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled == 0)
        {
            throw new InvalidOperationException($"Shader compile failed ({type}): " + _gl.GetShaderInfoLog(shader));
        }

        return shader;
    }

    /// <inheritdoc/>
    /// <summary>Deletes the GL program.</summary>
    /// <remarks>
    /// The call is not guarded by a disposed flag, so it must be issued exactly
    /// once; after deletion <see cref="Handle"/> is stale and must not be used.
    /// It requires the owning context to be current.
    /// </remarks>
    public void Dispose() => _gl.DeleteProgram(_program);
}
