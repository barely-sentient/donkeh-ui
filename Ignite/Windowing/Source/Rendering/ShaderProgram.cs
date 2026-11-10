using Silk.NET.OpenGL;

namespace Ignite.Windowing;

internal sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);

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

    public uint Handle => _program;

    public void Use() => _gl.UseProgram(_program);

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

    public void Uniform1(string name, int value) => _gl.Uniform1(Location(name), value);

    public void Uniform2(string name, float x, float y) => _gl.Uniform2(Location(name), x, y);

    public void Uniform3(string name, float x, float y, float z) =>
        _gl.Uniform3(Location(name), x, y, z);

    public void Uniform4(string name, float x, float y, float z, float w) =>
        _gl.Uniform4(Location(name), x, y, z, w);

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

    public void Dispose() => _gl.DeleteProgram(_program);
}
