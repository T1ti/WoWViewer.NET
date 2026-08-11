using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Input;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.Managers;
using WoWRenderLib.Providers;

namespace WoWRenderLib.DX11
{
    public struct WowClientConfig
    {
        public string wowDir = "";
        public string wowProduct = "";

        public string buildConfig = "";
        public string cdnConfig = "";

        public WowClientConfig()
        {
        }
    }

    public interface IImGuiBackend
    {
        void Initialize();
        void Update(float deltaTime);
        void Render();
        void Dispose();
    }

    public struct InputFrame
    {
        public Vector2 MousePosition;
        public bool LeftMouseDown;
        public bool RightMouseDown;

        public float MouseWheel;

        public HashSet<Key> KeysDown;
    }

    public class RendererStats
    {
        public double FrameTimeMs { get; internal set; }
        public double FPS { get; internal set; }

        public uint DrawCalls { get; internal set; }
        public uint VertexCount { get; internal set; }
    }

    public class WowViewerEngine : IDisposable
    {
        private WowClientConfig _wowConfig;

        private Dictionary<string, (string buildConfig, string cdnConfig)> _productList = new();

        private string[] _products = Array.Empty<string>(); // simple string list for IMGUI
        private int _currentProduct = -1; // list index for IMGUI

        // if both are true, it will autoload first product. just temporary convenience
        // hardcoded for now, will be depprecated when saving current product config
        private bool _SetDefaultProduct = true; // whether to auto set a default product (first one from build info list) or not, if none is set in config
        private bool _AutoLoadProduct = true; // whether to auto load the product or not. 

        public RendererStats Stats { get; } = new();
        public RendererSettings Settings { get; private set; } = new();
        public Vector3? InitialCameraPosition { get; set; }
        public Vector3? InitialCameraDirection { get; set; }

        private bool disposed = false;

        private IImGuiBackend? imgui;
        private readonly bool renderImGUI;

        private ComPtr<ID3D11Device> device = default;
        private ComPtr<ID3D11DeviceContext> deviceContext = default;
        private ComPtr<IDXGIFactory2> factory = default;

        private ComPtr<ID3D11Texture2D> sharedTexture = default;
        private ComPtr<ID3D11RenderTargetView> _sharedRTV = default;
        private ComPtr<IDXGIKeyedMutex> _keyedMutex = default;
        public ComPtr<ID3D11ShaderResourceView> SharedSRV { get; private set; }
        public uint SharedTextureWidth => (uint)viewportWidth;
        public uint SharedTextureHeight => (uint)viewportHeight;

        public bool IsInitialized = false;
        public bool IsInitializing = false;

        public bool UseKeyedMutex = false;

        private CompiledShader adtShaderProgram;
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private CompiledShader bboxShaderProgram;

        private float movementSpeed = 150f;
        private bool hasFocus = true;

        private Vector2 LastMousePosition;
        private Vector2? MouseDownPosition;
        private bool wasMouseDown = false;

        public Camera activeCamera { get; private set; }

        private int viewportWidth = 1;
        private int viewportHeight = 1;

        private bool shadersReady = false;

        private string WDTFDIDInput = "";

        private bool gizmoWasUsing = false;
        private bool gizmoWasOver = false;
        //  private ImGuizmoOperation currentGizmoOperation = ImGuizmoOperation.Translate;
        private bool wasSpacePressed = false;
        private bool showMapSelection = false;

        private ShaderManager shaderManager;
        private SceneManager sceneManager;
        private DBCManager dbcManager;

        // private ImGuiController imGuiController = null;

        private uint frameDelta = 0;

        // calcualte average fps
        private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
        private double _lastFpsTime;
        private uint _frameCount;
        private uint _maxDeltaMS = 500; // update fps every x ms

        private string[] wowProductList = [];
        private IntPtr _cachedSharedHandle = IntPtr.Zero;
        public WowViewerEngine(WowClientConfig wowConfig, IImGuiBackend? imguiBackend, bool renderImGUI)
        {
            _wowConfig = wowConfig;

            if (string.IsNullOrEmpty(_wowConfig.wowDir))
            {
                // try to get client path from registry on windows
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var installPath = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft", "InstallPath", null) as string;
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var lastDir = new DirectoryInfo(installPath).Name;
                        if (lastDir.StartsWith('_'))
                            installPath = Directory.GetParent(installPath.TrimEnd('\\'))?.FullName;

                        if (installPath != null)
                            _wowConfig.wowDir = installPath;
                    }
                    else
                    {
                        _wowConfig.wowDir = "C:\\World of Warcraft";
                    }
                }
            }

            LoadBuildInfo(_wowConfig.wowDir);

            this.imgui = imguiBackend;
            this.renderImGUI = renderImGUI;
        }

        // Load
        public void Initialize(DXGI dxgi, ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> deviceContext, Vector2D<int> frameBufferSize)
        {
            Console.WriteLine("Initializing WowViewerEngine..."); ;
            if (IsInitializing) return;

            IsInitializing = true;

            this.device = device;
            this.deviceContext = deviceContext;

            // TODO verify if this should be this project or UI app after split
            var exeLocation = Path.GetDirectoryName(AppContext.BaseDirectory);
            if (exeLocation == null)
            {
                Console.WriteLine("Could not determine executable location for shader loading");
                return;
            }

            shaderManager = new ShaderManager(device, Path.Combine(exeLocation, "Shaders"));
            sceneManager = new SceneManager(device, deviceContext, shaderManager);

            // imgui?.Initialize();

            adtShaderProgram = shaderManager.GetOrCompileShader("adt");
            wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
            m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
            bboxShaderProgram = shaderManager.GetOrCompileShader("boundingbox");

            sceneManager.Initialize(shaderManager, adtShaderProgram, wmoShaderProgram, m2ShaderProgram, bboxShaderProgram);

            shadersReady = true;

            WDTFDIDInput = sceneManager.CurrentWDTFileDataID.ToString();
            var startPos = InitialCameraPosition ?? new Vector3(3875f, -2050f, 616f); // quel'thalas, retail.
            if (!InitialCameraPosition.HasValue && _wowConfig.wowProduct == "wow_classic_era")
            {
                startPos = new Vector3(0, 0, 200); // alterac, wow classic.
            }

            // Init camera
            activeCamera = new Camera(
                startPos,
                yaw: 168f, pitch: 13f,
                aspectRatio: frameBufferSize.X / frameBufferSize.Y
            );
            if (InitialCameraDirection.HasValue)
                activeCamera.SetDirection(InitialCameraDirection.Value);
            activeCamera.FarPlane = Math.Max(Settings.TerrainRenderDistance, Settings.ModelRenderDistance);
            activeCamera.ModifyDirection(0, 0);

            ApplySettings(Settings);

            Resize((uint)frameBufferSize.X, (uint)frameBufferSize.Y);

            LoadCurrentProduct();
            IsInitialized = true;
            IsInitializing = false;

        }

        public unsafe void Resize(uint width, uint height)
        {
            if (width == 0 || height == 0)
                return;

            if (viewportWidth == (int)width && viewportHeight == (int)height)
                return;

            if (device.Handle == null)
                return;

            viewportWidth = (int)width;
            viewportHeight = (int)height;
            _cachedSharedHandle = IntPtr.Zero;

            if (activeCamera != null)
                activeCamera.AspectRatio = (float)width / (float)height;

            // Release old shared resources
            if (_keyedMutex.Handle != null) { _keyedMutex.Dispose(); _keyedMutex = default; }
            if (_sharedRTV.Handle != null) { _sharedRTV.Dispose(); _sharedRTV = default; }
            var oldSrv = SharedSRV;
            if (oldSrv.Handle != null) { oldSrv.Dispose(); SharedSRV = default; }
            if (sharedTexture.Handle != null) { sharedTexture.Dispose(); sharedTexture = default; }

            var texDesc = new Texture2DDesc
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                MiscFlags = UseKeyedMutex ? (uint)(ResourceMiscFlag.SharedKeyedmutex) : (uint)(ResourceMiscFlag.Shared)
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, null, ref sharedTexture));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(sharedTexture, null, ref _sharedRTV));
            ComPtr<ID3D11ShaderResourceView> srv = default;
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(sharedTexture, null, ref srv));
            SharedSRV = srv;
            if (UseKeyedMutex)
            {
                _keyedMutex = sharedTexture.QueryInterface<IDXGIKeyedMutex>();
            }

            sceneManager?.Resize(width, height, _sharedRTV);
        }

        public unsafe void AcquireSharedTextureForDisplay()
        {
            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.AcquireSync(1, unchecked((uint)-1));
        }

        public unsafe void ReleaseSharedTextureFromDisplay()
        {
            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.ReleaseSync(0);
        }

        public unsafe IntPtr GetSharedTextureHandle()
        {
            if (sharedTexture.Handle == null) return IntPtr.Zero;

            if (_cachedSharedHandle != IntPtr.Zero)
                return _cachedSharedHandle;

            var resource = sharedTexture.QueryInterface<IDXGIResource>();
            void* handle = null;
            SilkMarshal.ThrowHResult(resource.GetSharedHandle(&handle));
            resource.Dispose();
            _cachedSharedHandle = (IntPtr)handle;
            return _cachedSharedHandle;
        }
        public void Update(double deltaTime, InputFrame input)
        {
            HandleMouseLook(input, false, (float)deltaTime);
            HandleClickSelection(input, false);
            HandleKeyboardMovement(input, (float)deltaTime);


            /*
            if (renderImGUI)
            {
                imgui?.Update((float)deltaTime);

                bool gizmoInUse = gizmoWasUsing || gizmoWasOver;

                var io = ImGui.GetIO();
                HandleMouseLook(input, io, gizmoInUse, (float)deltaTime);
                HandleClickSelection(input, io, gizmoInUse);
                HandleKeyboardMovement(input, (float)deltaTime);
            }
            else
            {   HandleMouseLook(input, new ImGuiIOPtr(), false, (float)deltaTime);
                HandleClickSelection(input, new ImGuiIOPtr(), false);
                HandleKeyboardMovement(input, (float)deltaTime);
            }*/

            // TODO : we may need a special update function for controls/camera triggered by events if refresh rate isn't enough
            // eg a key could be pressed and released between two frame

        }

        public unsafe void Render(double deltaTime)
        {
            if (!IsInitialized) return;
            frameDelta = (uint)(deltaTime * 1000);

            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.AcquireSync(0, unchecked((uint)-1));

            sceneManager.UpdateTilesByCameraPos(activeCamera.Position);

            sceneManager.ProcessQueue();

            if (shadersReady)
            {
                (var drawCalls, var vertices) = sceneManager.RenderScene(activeCamera, out bool renderGizmoWasUsing, out bool renderGizmoWasOver);
                //if (renderImGUI)
                //    RenderGizmo();

                Stats.DrawCalls = drawCalls;
                Stats.VertexCount = vertices;

                gizmoWasUsing = renderGizmoWasUsing;
                gizmoWasOver = renderGizmoWasOver;
            }

            //if (renderImGUI)
            //    imgui?.Render();

            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.ReleaseSync(1);

            _frameCount++;

            // calculate average FPS over the last _maxDeltaMS milliseconds
            double now = _fpsWatch.Elapsed.TotalSeconds;
            double delta = now - _lastFpsTime;

            if (delta >= _maxDeltaMS / 1000.0)
            {
                Stats.FPS = _frameCount / (delta);
                Stats.FrameTimeMs = 1000.0 / Stats.FPS;

                _lastFpsTime = now;
                _frameCount = 0;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            _keyedMutex.Dispose();
            _sharedRTV.Dispose();
            SharedSRV.Dispose();
            sharedTexture.Dispose();
            shaderManager.Dispose();
            sceneManager?.Dispose();
            imgui?.Dispose();
            // inputContext?.Dispose();

            disposed = true;
        }

        private void LoadBuildInfo(string wowDirInput)
        {
            var buildInfoPath = Path.Combine(wowDirInput, ".build.info");

            var productList = new Dictionary<string, (string buildConfig, string cdnConfig)>();

            if (!Directory.Exists(wowDirInput) || !File.Exists(buildInfoPath))
            {
                Console.WriteLine("Invalid WoW directory or .build.info not found at " + buildInfoPath);
                return;
            }

            var buildInfo = File.ReadAllLines(buildInfoPath);

            var readFirstLine = false;
            foreach (var line in buildInfo)
            {
                if (!readFirstLine)
                {
                    readFirstLine = true;
                    continue;
                }
                var splitLine = line.Split('|');

                // TODO: Copy proper .build.info header parsing from WTL
                _productList[splitLine[14]] = (splitLine[2], splitLine[3]);
            }

            _products = _productList.Keys.ToArray();
            // _wowConfig.wowProduct = "wow";
            // optional, set first product as current if none is current yet
            // only if there's exactly one product for now to avoid not being able to switch
            if (_SetDefaultProduct && string.IsNullOrEmpty(_wowConfig.wowProduct) && _products.Length > 0)
            {
                _wowConfig.wowProduct = _products.First();
            }

            _currentProduct = Array.IndexOf(_products, _wowConfig.wowProduct);

            if (string.IsNullOrEmpty(_wowConfig.wowProduct) && _currentProduct == -1)
            {
                Console.WriteLine("Error : The WoW product (" + _wowConfig.wowProduct + ") set in config could not be found in .build.info.");
            }

        }

        void LoadCurrentProduct()
        {
            if (_currentProduct != -1)
            {
                var selectedProduct = _productList.ElementAt(_currentProduct);
                _wowConfig.wowProduct = selectedProduct.Key;
                _wowConfig.buildConfig = selectedProduct.Value.buildConfig;
                _wowConfig.cdnConfig = selectedProduct.Value.cdnConfig;

                StartCASCInitialization();
            }
        }

        private unsafe void RenderGizmo()
        {
            // TODO
        }

        private void StartCASCInitialization()
        {
            Task.Run(async () =>
            {
                await Services.CASC.Initialize(_wowConfig.wowProduct, _wowConfig.wowDir, _wowConfig.buildConfig, _wowConfig.cdnConfig);

                var tactFileProvider = new TACTSharpFileProvider();
                tactFileProvider.InitTACT(Services.CASC.buildInstance);
                FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
                FileProvider.SetProvider(tactFileProvider, TACTSharpFileProvider.BuildName);

                sceneManager.GetCurrentWDT();
                sceneManager.PreloadTEX();

                var dbcProvider = new DBCProvider();
                var dbdProvider = new DBDProvider();

                dbcManager = new DBCManager(dbdProvider, dbcProvider);
            });
        }

        public static Vector3 QuaternionToEuler(Quaternion q)
        {
            Vector3 euler;

            float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            euler.X = MathF.Atan2(sinr_cosp, cosr_cosp);

            float sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (MathF.Abs(sinp) >= 1)
                euler.Y = MathF.CopySign(MathF.PI / 2, sinp);
            else
                euler.Y = MathF.Asin(sinp);

            float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            euler.Z = MathF.Atan2(siny_cosp, cosy_cosp);

            return euler;
        }


        public void SetMovementSpeed(float speed)
        {
            movementSpeed = Math.Clamp(speed, 1f, 10_000f);
        }

        public void SetMouseSensitivity(float sensitivity)
        {
            Settings.MouseSensitivity = Math.Clamp(sensitivity, 0.001f, 2f);
        }

        public void ApplySettings(RendererSettings settings)
        {
            Settings = settings.Clone();
            SetMovementSpeed(Settings.MovementSpeed);
            SetMouseSensitivity(Settings.MouseSensitivity);

            if (activeCamera != null)
                activeCamera.FarPlane = Math.Max(Settings.TerrainRenderDistance, Settings.ModelRenderDistance);

            if (sceneManager != null)
            {
                sceneManager.TileLoadingDistance = Math.Clamp(Settings.TileLoadingDistance, 0, 32);
                sceneManager.TerrainRenderDistance = Settings.TerrainRenderDistance;
                sceneManager.ModelRenderDistance = Settings.ModelRenderDistance;
                sceneManager.RenderADT = Settings.RenderADT;
                sceneManager.RenderWMO = Settings.RenderWMO;
                sceneManager.RenderM2 = Settings.RenderM2;
                sceneManager.ShowBoundingBoxes = Settings.ShowBoundingBoxes;
                sceneManager.ShowBoundingSpheres = Settings.ShowBoundingSpheres;
            }
        }

        public void SetHasFocus(bool focus)
        {
            hasFocus = focus;
        }

        #region Inputs
        private void HandleMouseLook(InputFrame input, bool gizmoInUse, float deltaTime)
        {
            // Handle mouse look with right click
            if (input.RightMouseDown && !gizmoInUse)
            {
                var currentMousePos = input.MousePosition;

                if (LastMousePosition == default)
                {
                    LastMousePosition = currentMousePos;
                }
                else
                {
                    var lookSensitivity = Settings.MouseSensitivity;
                    var xOffset = (currentMousePos.X - LastMousePosition.X) * lookSensitivity;
                    var yOffset = (currentMousePos.Y - LastMousePosition.Y) * lookSensitivity;
                    LastMousePosition = currentMousePos;
                    activeCamera.ModifyDirection(-xOffset, yOffset);
                }
            }
            else
            {
                LastMousePosition = default;
            }
        }

        private void HandleClickSelection(InputFrame input, bool gizmoInUse)
        {
            bool mouseDownThisFrame = input.LeftMouseDown;

            if (mouseDownThisFrame && !wasMouseDown && !gizmoInUse)
            {
                MouseDownPosition = input.MousePosition;
            }

            if (!mouseDownThisFrame && wasMouseDown)
            {
                if (MouseDownPosition.HasValue)
                {
                    var dragDistance = Vector2.Distance(MouseDownPosition.Value, input.MousePosition);

                    if (dragDistance < 5.0f)
                    {
                        sceneManager.PerformRaycast(
                            input.MousePosition.X,
                            input.MousePosition.Y,
                            activeCamera,
                            viewportWidth,
                            viewportHeight
                        );
                    }
                }

                MouseDownPosition = null;
            }

            wasMouseDown = mouseDownThisFrame;
        }

        private void HandleKeyboardMovement(InputFrame input, float deltaTime)
        {
            if (!IsInitialized) return;
            float moveSpeed = movementSpeed * deltaTime;

            Vector3 currentPos = activeCamera.Position;
            float currentYaw = activeCamera.Yaw;

            if (input.KeysDown.Contains(Key.ShiftLeft))
                moveSpeed *= 2.0f;

            if (input.KeysDown.Contains(Key.ShiftLeft) && input.KeysDown.Contains(Key.ControlLeft))
                moveSpeed *= 4.0f;

            if (input.KeysDown.Contains(Key.AltLeft))
                moveSpeed *= 0.04f;

            var moveFront = Vector3.Normalize(new Vector3(activeCamera.Front.X, 0f, activeCamera.Front.Z));
            if (input.KeysDown.Contains(Key.W))
                activeCamera.Position += moveSpeed * activeCamera.Front;
            if (input.KeysDown.Contains(Key.S))
                activeCamera.Position -= moveSpeed * activeCamera.Front;
            if (input.KeysDown.Contains(Key.A))
                activeCamera.Position -= moveSpeed * activeCamera.Right;
            if (input.KeysDown.Contains(Key.D))
                activeCamera.Position += moveSpeed * activeCamera.Right;
            if (input.KeysDown.Contains(Key.Q))
                activeCamera.Position += moveSpeed * Vector3.UnitZ;
            if (input.KeysDown.Contains(Key.E))
                activeCamera.Position -= moveSpeed * Vector3.UnitZ;

            if (input.KeysDown.Contains(Key.I))
            {
                Console.WriteLine("ADT cache: " + ADTCache.GetCacheCount());
                Console.WriteLine("WMO cache: " + WMOCache.GetCacheCount());
                Console.WriteLine("M2 cache: " + M2Cache.GetCacheCount());
                Console.WriteLine("BLP cache: " + BLPCache.GetCacheCount());
            }

            if (input.KeysDown.Contains(Key.R))
            {
                var firstTile = sceneManager.GetFirstMapTile();
                var newPos = SceneManager.GetTileCenterPosition(firstTile.x, firstTile.y);
                activeCamera.Position = new Vector3(0, 0, 0);
            }

            if (input.KeysDown.Contains(Key.P))
                Console.WriteLine(activeCamera.Position);

            bool spacePressed = input.KeysDown.Contains(Key.Space);
            if (spacePressed && !wasSpacePressed && sceneManager.SelectedObject != null && !gizmoWasUsing)
            {
                // TODO: Gizmo
            }

            if(input.KeysDown.Contains(Key.Up))
                sceneManager.MoveSelectedObject(Vector3.UnitZ * moveSpeed);
            else if(input.KeysDown.Contains(Key.Down))
                sceneManager.MoveSelectedObject(-Vector3.UnitZ * moveSpeed);
            else if (input.KeysDown.Contains(Key.Left))
                sceneManager.MoveSelectedObject(-Vector3.UnitX * moveSpeed);
            else if (input.KeysDown.Contains(Key.Right))
                sceneManager.MoveSelectedObject(Vector3.UnitX * moveSpeed);
            else if(input.KeysDown.Contains(Key.Z))
                sceneManager.MoveSelectedObject(Vector3.UnitY * moveSpeed);
            else if (input.KeysDown.Contains(Key.X))
                sceneManager.MoveSelectedObject(-Vector3.UnitY * moveSpeed);

            //if (input.KeysDown.Contains(Key.ControlLeft) && input.KeysDown.Contains(Key.F))
            //    sceneManager.SaveManualJSON();

            wasSpacePressed = spacePressed;
        }
        #endregion
    }
}
