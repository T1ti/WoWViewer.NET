using Silk.NET.OpenGL;

namespace WoWViewer.NET.Managers
{
    public class ShaderManager : IDisposable
    {
        private readonly GL _gl;
        private readonly Dictionary<string, uint> _compiledShaders = [];
        private readonly Lock shaderLock = new();
        private static readonly Dictionary<string, DateTime> shaderMTimes = [];
        public bool shadersReady = false;
        private string shaderFolder;

        public ShaderManager(GL gl, string shaderFolder)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
            this.shaderFolder = shaderFolder;

            foreach (var file in Directory.GetFiles(shaderFolder, "*.shader"))
                shaderMTimes.Add(file, File.GetLastWriteTime(file));
        }

        public uint GetOrCompileShader(string type, bool forceRecompile = false)
        {
            if (_compiledShaders.TryGetValue(type, out var shaderProgram) && !forceRecompile)
                return shaderProgram;

            shaderProgram = CompileShader(type);
            _compiledShaders[type] = shaderProgram;
            return shaderProgram;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var shader in _compiledShaders.Values)
                    _gl.DeleteProgram(shader);

                _compiledShaders.Clear();
            }
        }

        public void CheckForChanges()
        {
            foreach (var file in Directory.GetFiles(shaderFolder, "*.shader"))
            {
                if (shaderMTimes[file] < File.GetLastWriteTime(file))
                {
                    shadersReady = false;
                    Console.WriteLine("Reloading shader " + file);

                    if (Path.GetFileNameWithoutExtension(file).StartsWith("adt"))
                        GetOrCompileShader("adt", true);
                    else if (Path.GetFileNameWithoutExtension(file).StartsWith("wmo"))
                        GetOrCompileShader("wmo", true);
                    else if (Path.GetFileNameWithoutExtension(file).StartsWith("m2"))
                        GetOrCompileShader("m2", true);
                    else if (Path.GetFileNameWithoutExtension(file).StartsWith("debug"))
                        GetOrCompileShader("debug", true);

                    shadersReady = true;

                    shaderMTimes[file] = File.GetLastWriteTime(file);
                }
            }
        }

        private uint CompileShader(string type)
        {
            string? fragmentSource;
            string? vertexSource;

            while (true)
            {
                try
                {
                    vertexSource = File.ReadAllText("Shaders/" + type + ".vertex.shader");
                    fragmentSource = File.ReadAllText("Shaders/" + type + ".fragment.shader");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading shader files: " + ex.Message);
                    Console.WriteLine("Retrying in 100ms");
                    Thread.Sleep(100);
                }
            }

            var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(vertexShader, vertexSource);
            _gl.CompileShader(vertexShader);
            _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vertexShaderStatus);

            var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(fragmentShader, fragmentSource);
            _gl.CompileShader(fragmentShader);
            _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fragmentShaderStatus);

            var shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(shaderProgram, vertexShader);
            _gl.AttachShader(shaderProgram, fragmentShader);
            _gl.BindFragDataLocation(shaderProgram, 0, "outColor");
            _gl.LinkProgram(shaderProgram);

            _gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out int programStatus);
            _gl.UseProgram(shaderProgram);

            if (programStatus == 0)
            {
                Console.WriteLine("Shader program failed to link.");

                Console.WriteLine("[" + type + "] [VERTEX] Shader compile status: " + vertexShaderStatus);
                _gl.GetShaderInfoLog(vertexShader, out string vertexShaderLog);
                Console.Write(vertexShaderLog);

                Console.WriteLine("[" + type + "] [FRAGMENT] Shader compile status: " + fragmentShaderStatus);
                _gl.GetShaderInfoLog(fragmentShader, out string fragmentShaderLog);
                Console.Write(fragmentShaderLog);

                Console.WriteLine("[" + type + "] [PROGRAM] Program link status: " + programStatus);
                var programInfoLog = _gl.GetProgramInfoLog(shaderProgram);
                Console.Write(programInfoLog);
            }

            _gl.ValidateProgram(shaderProgram);

            _gl.DetachShader(shaderProgram, vertexShader);
            _gl.DeleteShader(vertexShader);

            _gl.DetachShader(shaderProgram, fragmentShader);
            _gl.DeleteShader(fragmentShader);

            return shaderProgram;
        }
    }
}
