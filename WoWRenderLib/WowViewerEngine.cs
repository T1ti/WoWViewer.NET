using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWRenderLib.Cache;
using WoWRenderLib.Managers;
using WoWRenderLib.Objects;
using WoWRenderLib.Providers;
using WoWRenderLib.Services;

namespace WoWRenderLib
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

        public int DrawCalls { get; internal set; }
        public int VertexCount { get; internal set; }
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

        private bool disposed = false;

        private IImGuiBackend? imgui;
        private readonly bool renderImGUI;
        private GL gl;

        private bool cascLoaded = false;

        private uint adtShaderProgram;
        private uint wmoShaderProgram;
        private uint m2ShaderProgram;
        private uint debugShaderProgram;

        private float movementSpeed = 150f;
        private bool hasFocus = true;

        private Vector2 LastMousePosition;
        private Vector2? MouseDownPosition;
        private bool wasMouseDown = false;

        private Camera activeCamera;

        private int viewportWidth = 1;
        private int viewportHeight = 1;

        private bool shadersReady = false;

        private string WDTFDIDInput = "";

        private bool gizmoWasUsing = false;
        private bool gizmoWasOver = false;
        private ImGuizmoOperation currentGizmoOperation = ImGuizmoOperation.Translate;
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
                }
            }

            LoadBuildInfo(_wowConfig.wowDir);

            this.imgui = imguiBackend;
            this.renderImGUI = renderImGUI;
        }

        // Load
        public void Initialize(GL context, Vector2D<int> frameBufferSize)
        {
            gl = context;

            // TODO verify if this should be this project or UI app after split
            var exeLocation = Path.GetDirectoryName(AppContext.BaseDirectory);
            if (exeLocation == null)
            {
                Console.WriteLine("Could not determine executable location for shader loading");
                return;
            }

            shaderManager = new ShaderManager(gl, Path.Combine(exeLocation, "Shaders"));
            sceneManager = new SceneManager(gl, shaderManager);

            imgui?.Initialize();

            var err = gl.GetError();
            if (err != GLEnum.NoError)
            {
                Console.WriteLine("Render GL Error: " + err);
            }

            adtShaderProgram = shaderManager.GetOrCompileShader("adt");
            wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
            m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
            debugShaderProgram = shaderManager.GetOrCompileShader("debug");

            sceneManager.Initialize(shaderManager, adtShaderProgram, wmoShaderProgram, m2ShaderProgram, debugShaderProgram);
            shadersReady = true;

            WDTFDIDInput = sceneManager.CurrentWDTFileDataID.ToString();

            var startPos = new Vector3(5305f, -4122f, 92f);

            // Init camera
            activeCamera = new Camera(startPos, Vector3.UnitX, Vector3.UnitZ * -1, (float)frameBufferSize.X / (float)frameBufferSize.Y);
            activeCamera.Yaw = 168f;
            activeCamera.Pitch = 13f;
            activeCamera.ModifyDirection(0, 0); // hackfix: properly initializes camera


            // load product set in 
            LoadCurrentProduct();


            gl.Viewport(frameBufferSize);
            gl.ClearColor(1.0f, 0.0f, 0.0f, 1.0f);

            var err2 = gl.GetError();
            if (err2 != GLEnum.NoError)
            {
                Console.WriteLine("Load GL Error: " + err2);
            }

            unsafe
            {
                gl.DebugMessageCallback((source, type, id, severity, length, message, userparam) =>
                {
                    string msg = Marshal.PtrToStringAnsi(message, length);
                    if (id == 131185)
                        return;

                    Console.Error.WriteLine($"[DebugMessageCallback] source: {source}, type: {type}, id: {id}, severity {severity}, length {length}, userParam {userparam}\n{msg}\n\n");
                }, (void*)0);
            }
        }

        public void Resize(uint width, uint height)
        {
            // Debug.Assert(width > 0 && height > 0);

            if (viewportWidth == width && viewportHeight == height)
                return;

            viewportWidth = (int)width;
            viewportHeight = (int)height;

            activeCamera.AspectRatio = (float)width / (float)height;

            gl.Viewport(0, 0, width, height);
        }

        public void Update(double deltaTime, InputFrame input)
        {
            imgui?.Update((float)deltaTime);

            var io = ImGui.GetIO();
            bool gizmoInUse = gizmoWasUsing || gizmoWasOver;

            HandleMouseLook(input, io, gizmoInUse, (float)deltaTime);
            HandleClickSelection(input, io, gizmoInUse);
            HandleKeyboardMovement(input, (float)deltaTime);
        }

        public void Render(double deltaTime)
        {
            frameDelta = (uint)(deltaTime * 1000);

#if DEBUG
            var err = gl.GetError();
            if (err != GLEnum.NoError)
            {
                Console.WriteLine("Window Render GL Error: " + err);
            }
#endif

            gl.Enable(EnableCap.DepthTest);

            gl.ClearColor(0f, 0f, 0f, 0.5f);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            sceneManager.UpdateTilesByCameraPos(activeCamera.Position);

            sceneManager.ProcessQueue();

            if (imgui != null && renderImGUI)
            {
                if (!string.IsNullOrEmpty(sceneManager.StatusMessage))
                {
                    ImGui.Begin("Loading");
                    ImGui.Text(sceneManager.StatusMessage);
                    ImGui.End();
                }

                // always allow switching product if there are multiple
                if (!cascLoaded /*|| _products.Length > 0*/)
                {
                    BuildImGuiClientConfig();

                    imgui.Render();
                    return;
                }

                ImGui.Begin("Menu");
                if (ImGui.Button("Toggle map selection"))
                    showMapSelection = !showMapSelection;
                ImGui.End();

                if (showMapSelection)
                {
                    BuildImGuiMapSelection();
                }

                if (sceneManager.SceneLoaded)
                {
                    BuildImGuiSceneInfo();
                }

                if (sceneManager.SelectedObject != null)
                {
                    BuildImGuiSelectionInfo();
                }
            }

            if (shadersReady)
            {
                sceneManager.RenderScene(activeCamera, out bool renderGizmoWasUsing, out bool renderGizmoWasOver);
                RenderGizmo();
                sceneManager.RenderDebug(activeCamera, out bool debugGizmoWasUsing, out bool debugGizmoWasOver);

                gizmoWasUsing = renderGizmoWasUsing || debugGizmoWasUsing;
                gizmoWasOver = renderGizmoWasOver || debugGizmoWasOver;
            }

            if (renderImGUI)
                imgui?.Render();

            // the host must swap buffers after render
            // window.SwapBuffers();

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

            shaderManager.Dispose();
            sceneManager?.Dispose();
            imgui?.Dispose();
            // inputContext?.Dispose();
            // gl?.Dispose();

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

            // optional, set first product as current if none is current yet
            // only if there's exactly one product for now to avoid not being able to switch
            if (_SetDefaultProduct && string.IsNullOrEmpty(_wowConfig.wowProduct) && _products.Length == 1)
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


        #region ImGui builders
        private void BuildImGuiClientConfig()
        {
            ImGui.Begin("CASC setup");

            if (ImGui.BeginTabBar("Storage Type"))
            {
                if (ImGui.BeginTabItem("Local (fast, stable)"))
                {
                    var wowDirInput = _wowConfig.wowDir;
                    ImGui.InputText("Path to WoW directory", ref wowDirInput, 512);

                    if (!string.IsNullOrEmpty(wowDirInput))
                    {

                        if (_wowConfig.wowDir != wowDirInput)
                        {
                            _wowConfig.wowDir = wowDirInput;
                            LoadBuildInfo(wowDirInput);
                        }

                        ImGui.Combo("WoW Product", ref _currentProduct, _products, _products.Length);

                        if (ImGui.Button("Load (and wait a few seconds)"))
                            StartCASCInitialization();

                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Online (slow, unstable)"))
                {
                    if(wowProductList.Length == 0)
                    {
                        using(var httpClient = new HttpClient())
                        {
                            var summaryResponse = httpClient.GetStringAsync("https://us.version.battle.net/v2/summary").Result;
                            if (summaryResponse != null)
                            {
                                var tempProds = new List<string>();
                                foreach(var line in summaryResponse.Split("\n"))
                                {
                                    var splitLine = line.Split('|');
                                    if (splitLine[0].StartsWith("wow") && !splitLine[0].StartsWith("wowv") && !splitLine[0].StartsWith("wowdev") && string.IsNullOrEmpty(splitLine[2]))
                                        tempProds.Add(splitLine[0]);
                                }
                                wowProductList = [.. tempProds];
                            }
                        }
                    }

                    var products = wowProductList.ToArray();
                    var currentProduct = Array.IndexOf(products, _wowConfig.wowProduct);

                    ImGui.Combo("WoW Product", ref currentProduct, products, products.Length);

                    if (currentProduct != -1)
                    {
                        var selectedProduct = wowProductList.ElementAt(currentProduct);
                        _wowConfig.wowProduct = selectedProduct;

                        if (ImGui.Button("Load (and wait a few seconds)"))
                            StartCASCInitialization();
                    }

                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }

        private void BuildImGuiMapSelection()
        {
            ImGui.Begin("Map selection");

            var mapDB = dbcManager.GetOrLoad("Map", CASC.BuildName).Result;

            if (ImGui.BeginTable("MapTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("ID");
                ImGui.TableSetupColumn("Name");
                foreach (var mapRow in mapDB.Values)
                {
                    var mapID = (int)mapRow["ID"];
                    var mapDir = (string)mapRow["Directory"];
                    var mapName = (string)mapRow["MapName_lang"];

                    // Classic check
                    var wdtFileDataID = 0;

                    if (mapDB.AvailableColumns.Contains("WdtFileDataID"))
                    {
                        wdtFileDataID = (int)mapRow["WdtFileDataID"];
                    }
                    else
                    {
                        var jenkins = new TACTSharp.Jenkins96();
                        var filename = "World\\Maps\\" + mapDir + "\\" + mapDir + ".wdt";
                        var hash = jenkins.ComputeHash(filename);
                        var entries = CASC.buildInstance.Root!.GetEntriesByLookup(hash);
                        if (entries.Count > 0)
                            wdtFileDataID = (int)entries[0].fileDataID;
                    }

                    if (wdtFileDataID == 0 || !CASC.FileExists((uint)wdtFileDataID))
                        continue;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(mapDir);
                    ImGui.TableNextColumn();
                    ImGui.Text(mapName);
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable("Load " + wdtFileDataID.ToString()))
                    {
                        if (sceneManager.CurrentWDTFileDataID != wdtFileDataID && Services.CASC.FileExists((uint)wdtFileDataID))
                        {
                            ADTCache.ReleaseAll(gl);
                            WMOCache.ReleaseAll(gl);
                            M2Cache.ReleaseAll(gl);
                            BLPCache.ReleaseAll(gl);

                            sceneManager.LoadWDT((uint)wdtFileDataID);
                            sceneManager.PreloadTEX();
                            var firstTile = sceneManager.GetFirstMapTile();
                            var newPos = SceneManager.GetTileCenterPosition(firstTile.x, firstTile.y);
                            activeCamera.Position = newPos + new Vector3(0, 0, 100);
                        }
                    }
                }
                ImGui.EndTable();
            }
            ImGui.End();
        }

        private void BuildImGuiSceneInfo()
        {
            ImGui.Begin("3D debug");

            var renderADT = sceneManager.RenderADT;
            ImGui.Checkbox("Render ADT", ref renderADT);
            sceneManager.RenderADT = renderADT;

            var renderWMO = sceneManager.RenderWMO;
            ImGui.Checkbox("Render WMO", ref renderWMO);
            sceneManager.RenderWMO = renderWMO;

            var renderM2 = sceneManager.RenderM2;
            ImGui.Checkbox("Render M2", ref renderM2);
            sceneManager.RenderM2 = renderM2;

            var showBoundingBoxes = sceneManager.ShowBoundingBoxes;
            ImGui.Checkbox("Show Bounding Boxes", ref showBoundingBoxes);
            sceneManager.ShowBoundingBoxes = showBoundingBoxes;

            var showBoundingSpheres = sceneManager.ShowBoundingSpheres;
            ImGui.Checkbox("Show Bounding Spheres", ref showBoundingSpheres);
            sceneManager.ShowBoundingSpheres = showBoundingSpheres;

            var newPos = activeCamera.Position;
            ImGui.DragFloat3("Camera position", ref newPos);
            activeCamera.Position = newPos;

            var newFront = activeCamera.Front;
            ImGui.DragFloat3("Camera front", ref newFront);
            activeCamera.Front = newFront;

            ImGui.DragFloat("Camera movement speed", ref movementSpeed);

            var yaw = activeCamera.Yaw;
            ImGui.DragFloat("Camera yaw", ref yaw);
            activeCamera.Yaw = yaw;

            var pitch = activeCamera.Pitch;
            ImGui.DragFloat("Camera pitch", ref pitch);
            activeCamera.Pitch = pitch;

            var roll = activeCamera.Roll;
            ImGui.DragFloat("Camera roll", ref roll);
            activeCamera.Roll = roll;

            var lightDir = sceneManager.LightDirection;
            ImGui.DragFloat3("Light direction", ref lightDir);
            sceneManager.LightDirection = lightDir;

            ImGui.Text(sceneManager.SceneObjects.Count.ToString() + " objects (" + sceneManager.m2Instances.Count.ToString() + " unique M2, " + sceneManager.wmoInstances.Count.ToString() + " unique WMO, " + sceneManager.SceneObjects.Count(x => x is ADTContainer).ToString() + " ADT)");

            ImGui.Text("Cached: " + ADTCache.GetCacheCount() + " ADTs, " + WMOCache.GetCacheCount() + " WMOs, " + M2Cache.GetCacheCount() + " M2s, " + BLPCache.GetCacheCount() + " BLPs");

            var (x, y) = SceneManager.GetTileFromPosition(activeCamera.Position);

            ImGui.Text("Current ADT: " + x + ", " + y);

            ImGui.Text("Visible M2s: " + sceneManager.visibleM2s + ", Visible WMOs: " + sceneManager.visibleWMOs + ", Visible ADT chunks: " + sceneManager.visibleChunks);

            // if (frameDelta != 0)
            //     ImGui.Text("Frame time: " + frameDelta.ToString().PadLeft(3, ' ') + " ms (FPS: " + (1000 / frameDelta).ToString().PadLeft(3, ' ') + ")");

            // average fps/frame time
            if (Stats.FPS != 0)
            {
                ImGui.Text("Average FPS: " + Stats.FPS.ToString("F0") + ", Average Frame Time: " + Stats.FrameTimeMs.ToString("F2") + " ms");
            }

            var i = 0;
            if (ImGui.CollapsingHeader("Loaded WMOs"))
            {
                foreach (var sceneObject in sceneManager.SceneObjects.Where(x => x is WMOContainer))
                {
                    var wmoContainer = (WMOContainer)sceneObject;
                    var wmoString = "WMO #" + i + " FDID " + wmoContainer.FileDataId.ToString();

                    if (sceneObject.IsSelected)
                        wmoString += " (Selected)";

                    if (ImGui.CollapsingHeader(wmoString))
                    {
                        var curPos = wmoContainer.Position;
                        ImGui.DragFloat3("WMO Pos " + i, ref curPos);
                        wmoContainer.Position = curPos;

                        var curRot = wmoContainer.Rotation;
                        ImGui.DragFloat3("WMO Rot " + i, ref curRot);
                        wmoContainer.Rotation = curRot;

                        var curScale = wmoContainer.Scale;
                        ImGui.DragFloat("WMO Scale " + i, ref curScale, 0.01f);
                        wmoContainer.Scale = curScale;
                    }

                    i++;
                }
            }

            i = 0;
            if (ImGui.CollapsingHeader("Loaded M2s"))
            {
                foreach (var sceneObject in sceneManager.SceneObjects.Where(x => x is M2Container))
                {
                    var m2Container = (M2Container)sceneObject;
                    var m2String = "M2 #" + i + " FDID " + m2Container.FileDataId.ToString();

                    if (sceneObject.IsSelected)
                        m2String += " (Selected)";

                    if (ImGui.CollapsingHeader(m2String))
                    {
                        var curPos = m2Container.Position;
                        ImGui.DragFloat3("M2 Pos " + i, ref curPos);
                        m2Container.Position = curPos;

                        var curRot = m2Container.Rotation;
                        ImGui.DragFloat3("M2 Rot " + i, ref curRot);
                        m2Container.Rotation = curRot;

                        var curScale = m2Container.Scale;
                        ImGui.DragFloat("M2 Scale " + i, ref curScale, 0.01f);
                        m2Container.Scale = curScale;
                    }

                    i++;
                }
            }
            ImGui.End();
        }

        private void BuildImGuiSelectionInfo()
        {
            ImGui.Begin("Selected Object");
            ImGui.Text("Selected Object: " + sceneManager.SelectedObject.FileDataId);

            if (ImGui.Button("Deselect"))
            {
                sceneManager.SelectedObject.IsSelected = false;
                sceneManager.SelectedObject = null;
            }

            if (sceneManager.SelectedObject is WMOContainer selectedWMO)
            {
                ImGui.Text("Type: WMO");
                var curPos = selectedWMO.Position;
                ImGui.DragFloat3("Position", ref curPos);
                selectedWMO.Position = curPos;
                var curRot = selectedWMO.Rotation;
                ImGui.DragFloat3("Rotation", ref curRot);
                selectedWMO.Rotation = curRot;
                var curScale = selectedWMO.Scale;
                ImGui.DragFloat("Scale", ref curScale, 0.01f);
                selectedWMO.Scale = curScale;

                ImGui.Text("Doodad sets:");
                var doodadSets = selectedWMO.DoodadSets;
                for (var i = 0; i < doodadSets.Length; i++)
                {
                    var doodadSet = doodadSets[i];
                    var isEnabled = selectedWMO.EnabledDoodadSets[i];
                    if (ImGui.Checkbox("#" + i + ": " + doodadSet, ref isEnabled))
                    {
                        selectedWMO.ToggleDoodadSet(i);
                        //Console.WriteLine("Toggling WMO doodad set " + i + " (" + selectedWMO.DoodadSets[i] + ") in WMO " + selectedWMO.FileDataId + " from " + !selectedWMO.EnabledDoodadSets[i] + " to " + selectedWMO.EnabledDoodadSets[i]);
                    }
                }

                ImGui.Text("Groups:");
                var groups = selectedWMO.Groups;
                for (var i = 0; i < groups.Length; i++)
                {
                    var group = groups[i];
                    if (ImGui.Checkbox("#" + i + ": " + group, ref selectedWMO.EnabledGroups[i]))
                    {
                        //Console.WriteLine("Toggling WMO group " + i + " (" + selectedWMO.Groups[i] + ") in WMO " + selectedWMO.FileDataId + " from " + !selectedWMO.EnabledGroups[i] + " to " + selectedWMO.EnabledGroups[i]);
                        sceneManager.UpdateWMOInstanceList();
                    }
                }
            }
            else if (sceneManager.SelectedObject is M2Container selectedM2)
            {
                ImGui.Text("Type: M2");
                var curPos = selectedM2.Position;
                ImGui.DragFloat3("Position", ref curPos);
                selectedM2.Position = curPos;
                var curRot = selectedM2.Rotation;
                ImGui.DragFloat3("Rotation", ref curRot);
                selectedM2.Rotation = curRot;
                var curScale = selectedM2.Scale;
                ImGui.DragFloat("Scale", ref curScale, 0.01f);
                selectedM2.Scale = curScale;
            }

            ImGui.End();
        }

        #endregion

        private unsafe void RenderGizmo()
        {
            if (sceneManager.SelectedObject == null)
                return;

            ImGuizmo.BeginFrame();

            var windowPos = new Vector2(0, 0);
            var windowSize = new Vector2(viewportWidth, viewportHeight);

            ImGuizmo.SetDrawlist(ImGui.GetForegroundDrawList());
            ImGuizmo.Enable(true);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(windowPos.X, windowPos.Y, windowSize.X, windowSize.Y);

            var view = activeCamera.GetViewMatrix();
            view *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
            var proj = activeCamera.GetProjectionMatrix();

            var sceneObject = sceneManager.SelectedObject;
            var transform = Matrix4x4.CreateScale(sceneObject.Scale);
            transform *= Matrix4x4.CreateRotationX(MathF.PI / 180f * sceneObject.Rotation.X);
            transform *= Matrix4x4.CreateRotationY(MathF.PI / 180f * -sceneObject.Rotation.Z);
            transform *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (sceneObject.Rotation.Y - 270f));
            transform *= Matrix4x4.CreateTranslation(sceneObject.Position.X, sceneObject.Position.Z * -1, sceneObject.Position.Y);
            transform *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);
            ImGuizmo.PushID(0);

            if (ImGuizmo.Manipulate(ref view, ref proj, currentGizmoOperation, ImGuizmoMode.Local, ref transform))
            {
                var inversePostRot = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 270f);
                var unrotated = transform * inversePostRot;

                if (currentGizmoOperation == ImGuizmoOperation.Translate)
                {
                    var newPosition = new Vector3(unrotated.M41, unrotated.M43, -unrotated.M42);
                    sceneObject.Position = newPosition;
                }
                else if (currentGizmoOperation == ImGuizmoOperation.Rotate)
                {
                    if (Matrix4x4.Decompose(unrotated, out _, out Quaternion rotation, out _))
                    {
                        var euler = QuaternionToEuler(rotation);
                        var xRotationDegrees = euler.X * (180f / MathF.PI);
                        var yRotationDegrees = euler.Y * (180f / MathF.PI);
                        var zRotationDegrees = euler.Z * (180f / MathF.PI);
                        sceneObject.Rotation = new Vector3(
                            xRotationDegrees,
                            zRotationDegrees + 270f,
                            -yRotationDegrees
                        );
                    }
                }
                else if (currentGizmoOperation == ImGuizmoOperation.Scale)
                {
                    var scaleX = new Vector3(unrotated.M11, unrotated.M12, unrotated.M13).Length();
                    var scaleY = new Vector3(unrotated.M21, unrotated.M22, unrotated.M23).Length();
                    var scaleZ = new Vector3(unrotated.M31, unrotated.M32, unrotated.M33).Length();
                    sceneObject.Scale = (scaleX + scaleY + scaleZ) / 3f;
                }

                // TODO Fix, this is obviously going to be very slow on models with a lot of doodads.
                if (sceneObject is WMOContainer container)
                    container.OnDoodadSetsChanged?.Invoke(container);
            }

            ImGuizmo.PopID();
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

                cascLoaded = true;

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
            movementSpeed = speed;
        }

        public void SetHasFocus(bool focus)
        {
            hasFocus = focus;
        }

        #region Inputs
        private void HandleMouseLook(InputFrame input, ImGuiIOPtr io, bool gizmoInUse, float deltaTime)
        {
            // Handle mouse look with right click
            if (input.RightMouseDown && !io.WantCaptureMouse && !gizmoInUse)
            {
                var currentMousePos = input.MousePosition;

                if (LastMousePosition == default)
                {
                    LastMousePosition = currentMousePos;
                }
                else
                {
                    var lookSensitivity = 0.1f;
                    var xOffset = (currentMousePos.X - LastMousePosition.X) * lookSensitivity;
                    var yOffset = (LastMousePosition.Y - currentMousePos.Y) * lookSensitivity;
                    LastMousePosition = currentMousePos;
                    // var delta = input.MousePosition - LastMousePosition;

                    activeCamera.ModifyDirection(xOffset, yOffset);
                }
            }
            else
            {
                LastMousePosition = default;
            }
        }

        private void HandleClickSelection(InputFrame input, ImGuiIOPtr io, bool gizmoInUse)
        {
            bool mouseDownThisFrame = input.LeftMouseDown;

            if (mouseDownThisFrame && !wasMouseDown && !io.WantCaptureMouse && !gizmoInUse)
            {
                MouseDownPosition = input.MousePosition;
            }

            if (!mouseDownThisFrame && wasMouseDown)
            {
                if (MouseDownPosition.HasValue && !io.WantCaptureMouse)
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
            float moveSpeed = movementSpeed * deltaTime;

            if (input.KeysDown.Contains(Key.ShiftLeft))
                moveSpeed *= 2.0f;

            if (input.KeysDown.Contains(Key.W))
                activeCamera.Position += moveSpeed * activeCamera.Front;

            if (input.KeysDown.Contains(Key.S))
                activeCamera.Position -= moveSpeed * activeCamera.Front;

            if (input.KeysDown.Contains(Key.A))
                activeCamera.Position += Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;

            if (input.KeysDown.Contains(Key.D))
                activeCamera.Position -= Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;

            if (input.KeysDown.Contains(Key.Up) || input.KeysDown.Contains(Key.Q))
                activeCamera.Position -= moveSpeed * activeCamera.Up;

            if (input.KeysDown.Contains(Key.Down) || input.KeysDown.Contains(Key.E))
                activeCamera.Position += moveSpeed * activeCamera.Up;

            if (input.KeysDown.Contains(Key.R))
                activeCamera.Position = Vector3.One;

            bool spacePressed = input.KeysDown.Contains(Key.Space);
            if (spacePressed && !wasSpacePressed && sceneManager.SelectedObject != null && !gizmoWasUsing)
            {
                currentGizmoOperation = currentGizmoOperation switch
                {
                    ImGuizmoOperation.Translate => ImGuizmoOperation.Rotate,
                    ImGuizmoOperation.Rotate => ImGuizmoOperation.Scale,
                    ImGuizmoOperation.Scale => ImGuizmoOperation.Translate,
                    _ => ImGuizmoOperation.Translate
                };
                Console.WriteLine($"Gizmo mode switched to: {currentGizmoOperation}");
            }
            wasSpacePressed = spacePressed;
        }
        #endregion
    }
}
