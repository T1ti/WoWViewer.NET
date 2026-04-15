using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Windows.Controls;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using WoWRenderLib;
using WTEditor.SilkRenderer.WPF.OpenGL;

namespace WTEditor.Rendering
{
    public class WPFImGuiBackend : IImGuiBackend
    {
        public ImGuiController _controller { get; private set; }
        private GameControl _preview;

        // private IInputContext input;
        // private GL gl; static here

        public WPFImGuiBackend(GameControl preview)
        {
            this._preview = preview;

        }

        public void Initialize()
        {
            // Initialize ImGui with custom controller
            var width = (int)_preview.ActualWidth;
            var height = (int)_preview.ActualHeight;
            Debug.WriteLine($"Creating ImGuiController with Preview size: {width}x{height}");

            _controller = new ImGuiController(RenderContext.GL, width > 0 ? width : 800, height > 0 ? height : 600);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            // Initialize ImGui style
            ImGui.GetStyle().WindowRounding = 5.0f;
            ImGui.GetStyle().WindowPadding = new Vector2(0.0f, 0.0f);
            ImGui.GetStyle().FrameRounding = 12.0f;

            ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());

        }

        public void Update(float deltaTime)
        {
            _controller.Update(deltaTime);
        }

        public void Render()
        {
            _controller.Render();
        }

        public void Resize(int width, int height)
        {
            _controller.WindowResized(width, height);
        }

        public void Dispose()
        {
            _controller.Dispose();
        }
    }
}
