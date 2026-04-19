using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Runtime.CompilerServices;
using System.Text;

namespace WoWRenderLib.DX11.Managers
{
    public struct CompiledShader
    {
        public ComPtr<ID3D11VertexShader> VertexShader;
        public ComPtr<ID3D11PixelShader> PixelShader;
        public ComPtr<ID3D11InputLayout> InputLayout;
    }

    public class ShaderManager : IDisposable
    {
        private readonly D3DCompiler compiler = null!;
        private readonly ComPtr<ID3D11Device> device;

        private readonly Dictionary<string, CompiledShader> _compiledShaders = [];
        private readonly Lock shaderLock = new();

        private static readonly Dictionary<string, DateTime> shaderMTimes = [];
        public bool shadersReady = false;
        private readonly string shaderFolder;

        public ShaderManager(ComPtr<ID3D11Device> device, string shaderFolder)
        {
            this.device = device;
            compiler = D3DCompiler.GetApi();

            this.shaderFolder = shaderFolder;

            foreach (var file in Directory.GetFiles(shaderFolder, "*.hlsl"))
                shaderMTimes.Add(file, File.GetLastWriteTime(file));
        }

        public CompiledShader GetOrCompileShader(string type, bool forceRecompile = false)
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
                compiler.Dispose();

                // TODO: Delete DX11 shaders
            }
        }

        public bool CheckForChanges()
        {
            foreach (var file in Directory.GetFiles(shaderFolder, "*.hlsl"))
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

                    return true;
                }
            }

            return false;
        }

        private unsafe CompiledShader CompileShader(string type)
        {
            string? shaderSource;

            while (true)
            {
                try
                {
                    shaderSource = File.ReadAllText("Shaders/" + type + ".hlsl");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading shader files: " + ex.Message);
                    Console.WriteLine("Retrying in 100ms");
                    Thread.Sleep(100);
                }
            }

            var shaderBytes = Encoding.ASCII.GetBytes(shaderSource);
            ComPtr<ID3D11VertexShader> vertexShader = default;
            ComPtr<ID3D11PixelShader> pixelShader = default;

            // Compile vertex shader.
            ComPtr<ID3D10Blob> vertexCode = default;
            ComPtr<ID3D10Blob> vertexErrors = default;
            HResult hr = compiler.Compile
            (
                in shaderBytes[0],
                (nuint)shaderBytes.Length,
                nameof(shaderSource),
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                "VS_Main",
                "vs_5_0",
                0,
                0,
                ref vertexCode,
                ref vertexErrors
            );

            // Check for compilation errors.
            if (hr.IsFailure)
            {
                if (vertexErrors.Handle is not null)
                {
                    Console.WriteLine(SilkMarshal.PtrToString((nint)vertexErrors.GetBufferPointer()));
                }

                hr.Throw();
            }

            // Compile pixel shader.
            ComPtr<ID3D10Blob> pixelCode = default;
            ComPtr<ID3D10Blob> pixelErrors = default;
            hr = compiler.Compile
            (
                in shaderBytes[0],
                (nuint)shaderBytes.Length,
                nameof(shaderSource),
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                "PS_Main",
                "ps_5_0",
                0,
                0,
                ref pixelCode,
                ref pixelErrors
            );

            // Check for compilation errors.
            if (hr.IsFailure)
            {
                if (pixelErrors.Handle is not null)
                {
                    Console.WriteLine(SilkMarshal.PtrToString((nint)pixelErrors.GetBufferPointer()));
                }

                hr.Throw();
            }

            // Create vertex shader.
            SilkMarshal.ThrowHResult
            (
                device.CreateVertexShader
                (
                    vertexCode.GetBufferPointer(),
                    vertexCode.GetBufferSize(),
                    ref Unsafe.NullRef<ID3D11ClassLinkage>(),
                    ref vertexShader
                )
            );

            // Create pixel shader.
            SilkMarshal.ThrowHResult
            (
                device.CreatePixelShader
                (
                    pixelCode.GetBufferPointer(),
                    pixelCode.GetBufferSize(),
                    ref Unsafe.NullRef<ID3D11ClassLinkage>(),
                    ref pixelShader
                )
            );

            ComPtr<ID3D11InputLayout> inputLayout = default;

            // TODO: I don't think this is a good way of doing this
            if (type == "adt")
            {
                fixed (byte* posName = SilkMarshal.StringToMemory("POSITION"))
                fixed (byte* normalName = SilkMarshal.StringToMemory("NORMAL"))
                fixed (byte* colorName = SilkMarshal.StringToMemory("COLOR"))
                fixed (byte* texCoordName = SilkMarshal.StringToMemory("TEXCOORD"))
                {
                    var inputElements = new InputElementDesc[]
                    {
                    new()
                    {
                        SemanticName = posName,
                        SemanticIndex = 0,
                        Format = Format.FormatR32G32B32Float,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                        InstanceDataStepRate = 0
                    },
                    new()
                    {
                        SemanticName = normalName,
                        SemanticIndex = 0,
                        Format = Format.FormatR32G32B32Float,
                        InputSlot = 0,
                        AlignedByteOffset = uint.MaxValue, // AUTO
                        InputSlotClass = InputClassification.PerVertexData,
                        InstanceDataStepRate = 0
                    },
                    new()
                    {
                        SemanticName = texCoordName,
                        SemanticIndex = 0, // TEXCOORD0
                        Format = Format.FormatR32G32Float,
                        InputSlot = 0,
                        AlignedByteOffset = uint.MaxValue, // AUTO
                        InputSlotClass = InputClassification.PerVertexData,
                        InstanceDataStepRate = 0
                    },
                    new() {
                        SemanticName = colorName,
                        SemanticIndex = 0, // COLOR0
                        Format = Format.FormatR32G32B32A32Float,
                        InputSlot = 0,
                        AlignedByteOffset = uint.MaxValue, // AUTO
                        InputSlotClass = InputClassification.PerVertexData,
                        InstanceDataStepRate = 0
                    },
                    };

                    SilkMarshal.ThrowHResult
                    (
                        device.CreateInputLayout
                        (
                            in inputElements[0],
                            (uint)inputElements.Length,
                            vertexCode.GetBufferPointer(),
                            vertexCode.GetBufferSize(),
                            ref inputLayout
                        )
                    );
                }
            }
            else if (type == "wmo")
            {
                fixed (byte* posName = SilkMarshal.StringToMemory("POSITION"))
                fixed (byte* normalName = SilkMarshal.StringToMemory("NORMAL"))
                fixed (byte* colorName = SilkMarshal.StringToMemory("COLOR"))
                fixed (byte* texCoordName = SilkMarshal.StringToMemory("TEXCOORD"))
                {
                    var inputElements = new InputElementDesc[]
                    {
                        // Buffer 0
                        new()
                        {
                            SemanticName = posName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32Float,
                            InputSlot = 0,
                            AlignedByteOffset = 0,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = normalName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 1,
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 2,
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 3,
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = colorName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = colorName,
                            SemanticIndex = 1,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = colorName,
                            SemanticIndex = 2,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },

                        // Buffer 1
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 4,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = 0,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 5,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 6,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 7,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                    };

                    SilkMarshal.ThrowHResult
                    (
                        device.CreateInputLayout
                        (
                            in inputElements[0],
                            (uint)inputElements.Length,
                            vertexCode.GetBufferPointer(),
                            vertexCode.GetBufferSize(),
                            ref inputLayout
                        )
                    );
                }
            }
            else if (type == "m2")
            {
                fixed (byte* posName = SilkMarshal.StringToMemory("POSITION"))
                fixed (byte* normalName = SilkMarshal.StringToMemory("NORMAL"))
                fixed (byte* texCoordName = SilkMarshal.StringToMemory("TEXCOORD"))
                {
                    var inputElements = new InputElementDesc[]
                    {
                        // Buffer 0
                        new()
                        {
                            SemanticName = posName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32Float,
                            InputSlot = 0,
                            AlignedByteOffset = 0,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = normalName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue, // AUTO
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 0, // TEXCOORD0
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue, // AUTO
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 1, // TEXCOORD1
                            Format = Format.FormatR32G32Float,
                            InputSlot = 0,
                            AlignedByteOffset = uint.MaxValue, // AUTO
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },

                        // Buffer 1
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 2,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = 0,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 3,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 4,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                        new()
                        {
                            SemanticName = texCoordName,
                            SemanticIndex = 5,
                            Format = Format.FormatR32G32B32A32Float,
                            InputSlot = 1,
                            AlignedByteOffset = uint.MaxValue,
                            InputSlotClass = InputClassification.PerInstanceData,
                            InstanceDataStepRate = 1
                        },
                    };

                    SilkMarshal.ThrowHResult
                    (
                        device.CreateInputLayout
                        (
                            in inputElements[0],
                            (uint)inputElements.Length,
                            vertexCode.GetBufferPointer(),
                            vertexCode.GetBufferSize(),
                            ref inputLayout
                        )
                    );
                }
            }
            else if (type == "boundingbox")
            {
                fixed (byte* posName = SilkMarshal.StringToMemory("POSITION"))
                {
                    var inputElements = new InputElementDesc[]
                    {
                        new()
                        {
                            SemanticName = posName,
                            SemanticIndex = 0,
                            Format = Format.FormatR32G32B32Float,
                            InputSlot = 0,
                            AlignedByteOffset = 0,
                            InputSlotClass = InputClassification.PerVertexData,
                            InstanceDataStepRate = 0
                        },
                    };

                    SilkMarshal.ThrowHResult
                    (
                        device.CreateInputLayout
                        (
                            in inputElements[0],
                            (uint)inputElements.Length,
                            vertexCode.GetBufferPointer(),
                            vertexCode.GetBufferSize(),
                            ref inputLayout
                        )
                    );
                }
            }
            else
            {
                throw new NotImplementedException("No input layout defined for unknown shader type: " + type);
            }

            // Clean up any resources.
            vertexCode.Dispose();
            vertexErrors.Dispose();
            pixelCode.Dispose();
            pixelErrors.Dispose();

            return new CompiledShader
            {
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                InputLayout = inputLayout
            };
        }
    }
}
