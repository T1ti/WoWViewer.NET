using Silk.NET.OpenGL;

namespace WoWViewer.NET
{
    public static class ShaderCompiler
    {
        public static uint CompileShader(string type)
        {
            var _gl = Program.gl;
            // Print OpenGL version/vendor
            Console.WriteLine("OpenGL version: " + _gl.GetStringS(StringName.Version));
            Console.WriteLine("OpenGL vendor: " + _gl.GetStringS(StringName.Vendor));

            var vertexShader = _gl.CreateShader(ShaderType.VertexShader);

            var vertexSource = File.ReadAllText("Shaders/" + type + ".vertex.shader");
            _gl.ShaderSource(vertexShader, vertexSource);

            _gl.CompileShader(vertexShader);

            _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vertexShaderStatus);
            Console.WriteLine("[" + type + "] [VERTEX] Shader compile status: " + vertexShaderStatus);

            _gl.GetShaderInfoLog(vertexShader, out string vertexShaderLog);
            Console.Write(vertexShaderLog);

            // Fragment shader
            var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);

            var fragmentSource = File.ReadAllText("Shaders/" + type + ".fragment.shader");
            _gl.ShaderSource(fragmentShader, fragmentSource);

            _gl.CompileShader(fragmentShader);

            _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fragmentShaderStatus);
            Console.WriteLine("[" + type + "] [FRAGMENT] Shader compile status: " + fragmentShaderStatus);

            _gl.GetShaderInfoLog(fragmentShader, out string fragmentShaderLog);
            Console.Write(fragmentShaderLog);

            // Shader program
            var shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(shaderProgram, vertexShader);
            _gl.AttachShader(shaderProgram, fragmentShader);

            _gl.BindFragDataLocation(shaderProgram, 0, "outColor");

            _gl.LinkProgram(shaderProgram);
            var programInfoLog = _gl.GetProgramInfoLog(shaderProgram);
            Console.Write(programInfoLog);

            _gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out int programStatus);
            Console.WriteLine("[" + type + "] [PROGRAM] Program link status: " + programStatus);
            _gl.UseProgram(shaderProgram);

            _gl.ValidateProgram(shaderProgram);

            _gl.DetachShader(shaderProgram, vertexShader);
            _gl.DeleteShader(vertexShader);

            _gl.DetachShader(shaderProgram, fragmentShader);
            _gl.DeleteShader(fragmentShader);

            return shaderProgram;
        }
    }
}
