using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace WoWViewer.NET.Utils
{
    public static class ImGuiExtensions
    {
        public static void DrawMatrix4x4(string label, Matrix4x4 matrix)
        {
            ImGui.Text(label);
            ImGui.BeginTable(label, 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchSame, new Vector2(400, 100));
            ImGui.TableSetupColumn("X");
            ImGui.TableSetupColumn("Y");
            ImGui.TableSetupColumn("Z");
            ImGui.TableSetupColumn("W");
            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            ImGui.Text(matrix.M11.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M12.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M13.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M14.ToString());

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M21.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M22.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M23.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M24.ToString());

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M31.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M32.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M33.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M34.ToString());

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M41.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M42.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M43.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(matrix.M44.ToString());
            ImGui.EndTable();
        }
    }
}
