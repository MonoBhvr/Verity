using Verity.Core;
using Verity.Core.ECS;

namespace Verity.Graphics;

public enum CameraOutputTarget
{
    MainWindow = 0,
    RenderTexture = 1,
    Window = 2
}

public enum CameraOutputSamplingMode
{
    Nearest = 0,
    Linear = 1
}

[RequireComponent(typeof(Camera))]
public class CameraOutput : Component
{
    [SerializeField]
    public CameraOutputTarget Target { get; set; } = CameraOutputTarget.MainWindow;

    [SerializeField]
    public bool Primary { get; set; } = true;

    [SerializeField]
    public int Order { get; set; }

    [SerializeField]
    public string OutputName { get; set; } = string.Empty;

    [SerializeField]
    public CameraTextureAsset TargetTexture { get; set; } = new();

    [SerializeField]
    public CameraOutputSamplingMode SamplingMode { get; set; } = CameraOutputSamplingMode.Linear;

    [SerializeField]
    public bool WindowVisible { get; set; } = true;

    [SerializeField]
    public bool WindowDecorated { get; set; } = true;

    [SerializeField]
    public string WindowGroup { get; set; } = string.Empty;

    [SerializeField]
    public Vector2 WindowPosition { get; set; } = new(32f, 32f);

    [SerializeField]
    public Vector2 WindowSize { get; set; } = new(320f, 180f);

    [SerializeField]
    public bool WindowLockPosition { get; set; }

    [SerializeField]
    public bool WindowLockSize { get; set; }

    [SerializeField]
    public bool WindowLockAspect { get; set; } = true;

    [SerializeField]
    public bool WindowCloseQuitsApplication { get; set; } = true;

    public Camera? Camera => Owner.GetComponent<Camera>();

    public string ResolveOutputName()
    {
        if (!string.IsNullOrWhiteSpace(OutputName))
            return OutputName.Trim();

        if (!string.IsNullOrWhiteSpace(TargetTexture.Path))
            return AssetPathUtility.Normalize(TargetTexture.Path);

        return Owner.Id.ToString("N");
    }

    public CameraTextureAssetData GetRenderTextureSettings()
    {
        if (Target == CameraOutputTarget.Window)
        {
            return new CameraTextureAssetData
            {
                Width = Math.Max(1, (int)MathF.Round(WindowSize.X)),
                Height = Math.Max(1, (int)MathF.Round(WindowSize.Y))
            };
        }

        if (string.IsNullOrWhiteSpace(TargetTexture.Path))
            return new CameraTextureAssetData();

        return TargetTexture.LoadSettings(RenderPipeline.BaseAssetsPath);
    }

    public void SaveRenderTextureSettings(CameraTextureAssetData settings)
    {
        if (string.IsNullOrWhiteSpace(TargetTexture.Path))
            return;

        TargetTexture.SaveSettings(settings, RenderPipeline.BaseAssetsPath);
    }

    public void ResizeRenderTexture(int width, int height)
    {
        if (string.IsNullOrWhiteSpace(TargetTexture.Path))
            return;

        TargetTexture.Resize(width, height, RenderPipeline.BaseAssetsPath);
    }
}
