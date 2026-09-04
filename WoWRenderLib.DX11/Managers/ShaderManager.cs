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

        // File timestamps belong to this manager instance. Keeping them static makes
        // recreating a renderer (for example after applying settings) try to add the
        // same shader paths to an already-populated dictionary.
        private readonly Dictionary<string, DateTime> shaderMTimes = [];
        public bool shadersReady = false;
        private readonly string shaderFolder;

        public ShaderManager(ComPtr<ID3D11Device> device, string shaderFolder)
        {
            this.device = device;
            compiler = D3DCompiler.GetApi();

            this.shaderFolder = Path.GetFullPath(shaderFolder);

            if (!Directory.Exists(this.shaderFolder))
                throw new DirectoryNotFoundException($"Shader directory was not found: {this.shaderFolder}");

            foreach (var file in Directory.GetFiles(this.shaderFolder, "*.hlsl"))
                shaderMTimes.Add(file, File.GetLastWriteTime(file));
        }

        public CompiledShader GetOrCompileShader(string type, bool forceRecompile = false)
        {
            return GetOrCompileShader(type, null, true, forceRecompile);
        }

        public CompiledShader GetOrCompileAdtShader(
            int layerCount,
            bool useHeightTextures,
            bool forceRecompile = false)
        {
            if (layerCount is not (1 or 2 or 4 or 8))
                throw new ArgumentOutOfRangeException(nameof(layerCount));

            return GetOrCompileShader("adt", layerCount, useHeightTextures, forceRecompile);
        }

        private CompiledShader GetOrCompileShader(
            string type,
            int? adtLayerCount,
            bool adtUseHeightTextures,
            bool forceRecompile)
        {
            var cacheKey = adtLayerCount.HasValue
                ? $"{type}@{adtLayerCount}@height-{adtUseHeightTextures}"
                : type;
            if (_compiledShaders.TryGetValue(cacheKey, out var shaderProgram) && !forceRecompile)
                return shaderProgram;

            shaderProgram = CompileShader(type, adtLayerCount, adtUseHeightTextures);
            if (_compiledShaders.Remove(cacheKey, out var previous))
                DisposeShader(previous);
            _compiledShaders[cacheKey] = shaderProgram;
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
                    DisposeShader(shader);

                _compiledShaders.Clear();
                compiler.Dispose();
            }
        }

        private static void DisposeShader(CompiledShader shader)
        {
            shader.InputLayout.Dispose();
            shader.PixelShader.Dispose();
            shader.VertexShader.Dispose();
        }

        public bool CheckForChanges()
        {
            foreach (var file in Directory.GetFiles(shaderFolder, "*.hlsl"))
            {
                var modified = File.GetLastWriteTime(file);
                if (!shaderMTimes.TryGetValue(file, out var previousModified))
                {
                    shaderMTimes[file] = modified;
                    continue;
                }

                if (previousModified < modified)
                {
                    shadersReady = false;
                    Console.WriteLine("Reloading shader " + file);

                    if (Path.GetFileNameWithoutExtension(file).StartsWith("adt"))
                    {
                        GetOrCompileShader("adt", true);
                        foreach (var layerCount in new[] { 1, 2, 4, 8 })
                        {
                            GetOrCompileAdtShader(layerCount, false, true);
                            GetOrCompileAdtShader(layerCount, true, true);
                        }
                    }
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

        private unsafe CompiledShader CompileShader(
            string type,
            int? adtLayerCount,
            bool adtUseHeightTextures)
        {
            var shaderPath = Path.Combine(shaderFolder, type + ".hlsl");
            var shaderSource = File.ReadAllText(shaderPath);
            if (adtLayerCount.HasValue)
            {
                shaderSource =
                    $"#define ADT_LAYER_COUNT {adtLayerCount.Value}\n" +
                    $"#define ADT_USE_HEIGHT_TEXTURES {(adtUseHeightTextures ? 1 : 0)}\n" +
                    shaderSource;
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
                {
                    var inputElements = new InputElementDesc[]
                    {
                    new()
                    {
                        SemanticName = posName,
                        SemanticIndex = 0,
                        Format = Format.FormatR32Float,
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
