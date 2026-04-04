using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWViewer.NET.Renderer
{
    public class DebugRenderer
    {
        private readonly GL gl;
        private uint vao;
        private uint vbo;
        private uint shaderProgram;
        private List<DebugVertex> vertices = [];
        private int allocatedVertices = 100000;

        [StructLayout(LayoutKind.Sequential)]
        private struct DebugVertex(Vector3 position, Vector4 color)
        {
            public Vector3 Position = position;
            public Vector4 Color = color;
        }

        public unsafe DebugRenderer(GL gl, uint shaderProgram)
        {
            this.gl = gl;
            this.shaderProgram = shaderProgram;

            vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();

            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(allocatedVertices * 28), null, BufferUsageARB.DynamicDraw); // dynamicdraw since this changes often

            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, (void*)0);
            gl.EnableVertexAttribArray(0);

            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (void*)12);
            gl.EnableVertexAttribArray(1);

            gl.BindVertexArray(0);
        }

        public unsafe void Render(Matrix4x4 projection, Matrix4x4 view)
        {
            if (vertices.Count == 0)
                return;

            gl.UseProgram(shaderProgram);

            var projLoc = gl.GetUniformLocation(shaderProgram, "projection_matrix");
            var viewLoc = gl.GetUniformLocation(shaderProgram, "view_matrix");

            gl.UniformMatrix4(projLoc, 1, false, (float*)&projection);
            gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);

            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            if (vertices.Count > allocatedVertices)
            {
                allocatedVertices = vertices.Count * 2; // be greedy for now
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(allocatedVertices * sizeof(DebugVertex)), null, BufferUsageARB.DynamicDraw);
            }

            var vertexArray = vertices.ToArray();
            fixed (DebugVertex* vertexPtr = vertexArray)
            {
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexArray.Length * sizeof(DebugVertex)), vertexPtr);
            }

            var oldDepthTest = gl.IsEnabled(EnableCap.DepthTest);
            gl.Disable(EnableCap.DepthTest);

            gl.LineWidth(3.0f);

            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)vertices.Count);

            if (oldDepthTest)
                gl.Enable(EnableCap.DepthTest);

            gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            gl.DeleteVertexArray(vao);
            gl.DeleteBuffer(vbo);
        }

        public void Clear()
        {
            vertices.Clear();
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color)
        {
            vertices.Add(new DebugVertex(start, color));
            vertices.Add(new DebugVertex(end, color));
        }

        public void DrawBox(Vector3 min, Vector3 max, Vector4 color)
        {
            DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color);
            DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color);
            DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color);
            DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), color);

            DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
            DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color);
            DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
            DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), color);

            DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color);
            DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
            DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color);
            DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
        }

        // this is a full yoink
        public void DrawSphere(Vector3 center, float radius, Vector4 color, int segments = 16)
        {
            float angleStep = MathF.PI * 2.0f / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;
                Vector3 p1 = center + new Vector3(MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius, 0);
                Vector3 p2 = center + new Vector3(MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius, 0);
                DrawLine(p1, p2, color);
            }

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;
                Vector3 p1 = center + new Vector3(MathF.Cos(angle1) * radius, 0, MathF.Sin(angle1) * radius);
                Vector3 p2 = center + new Vector3(MathF.Cos(angle2) * radius, 0, MathF.Sin(angle2) * radius);
                DrawLine(p1, p2, color);
            }

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep;
                float angle2 = (i + 1) * angleStep;
                Vector3 p1 = center + new Vector3(0, MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius);
                Vector3 p2 = center + new Vector3(0, MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius);
                DrawLine(p1, p2, color);
            }
        }
    }
}
