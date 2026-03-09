using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public class ScreenWindow : EditorWindow
{
    private readonly EditorApp _app;

    public ScreenWindow(EditorApp app) : base("Screen")
    {
        _app = app;
    }

    public override void OnGui()
    {
        var contentSize = ImGui.GetContentRegionAvail();
        int width = (int)contentSize.X;
        int height = (int)contentSize.Y;

        if (width <= 0 || height <= 0) return;

        _app.RenderPipeline.EnsureScreenFbo(width, height);

        var world = WorldManager.ActiveWorld;
        Camera? camera = null;
        if (world != null)
        {
            camera = FindWorldCamera(world);
            if (camera != null)
            {
                // We pass the full window size, the pipeline handles the sub-viewport
                _app.RenderPipeline.RenderWorld(world, camera, _app.RenderPipeline.ScreenFbo);
            }
        }

        var colorTex = _app.RenderPipeline.ScreenColorTexture;
        if (colorTex is OpenGlTexture glTex)
        {
            unsafe
            {
                var texRef = new ImTextureRef(null, new ImTextureID((nint)glTex.Id));
                ImGui.Image(texRef, contentSize, new Vector2(0, 1), new Vector2(1, 0));
            }

            if (camera != null)
            {
                HandleInteraction(camera, ImGui.GetItemRectMin(), ImGui.GetItemRectSize());
            }
        }
    }

    private void HandleInteraction(Camera camera, Vector2 imgMin, Vector2 imgSize)
    {
        if (ImGui.IsItemHovered())
        {
            // SDL 전체 윈도우 마우스 좌표를 ImGui 이미지 내의 상대 픽셀 좌표로 변환
            var mouseAbs = new Vector2(Verity.Input.Input.MousePosition.X, Verity.Input.Input.MousePosition.Y);
            var localMouse = mouseAbs - imgMin;
            
            // localMouse는 이제 이미지 좌상단(0,0) 기준 픽셀 좌표입니다.
            // camera.ScreenToWorld는 이 좌표와 내부의 _viewportX/Y를 사용하여 정확한 월드 좌표를 계산합니다.
        }
    }

    private static Camera? FindWorldCamera(World world)
    {
        foreach (var entity in world.RootEntities)
        {
            var cam = FindCameraRecursive(entity);
            if (cam != null)
                return cam;
        }
        return null;
    }

    private static Camera? FindCameraRecursive(Entity entity)
    {
        if (!entity.Active) return null;

        var cam = entity.GetComponent<Camera>();
        if (cam != null && cam.Enabled)
            return cam;

        foreach (var child in entity.Transform.Children)
        {
            var found = FindCameraRecursive(child.Owner);
            if (found != null)
                return found;
        }
        return null;
    }
}
