using Verity.Graphics;

namespace Verity.Tests;

public class CameraTextureAssetTests
{
    [Fact]
    public void CameraTextureAssetData_SaveAndLoad_PreservesSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VerityTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Preview.rendertexture");

        try
        {
            var data = new CameraTextureAssetData
            {
                Width = 320,
                Height = 180
            };

            data.Save(path);

            var loaded = CameraTextureAssetData.Load(path);

            Assert.Equal(320, loaded.Width);
            Assert.Equal(180, loaded.Height);
            Assert.True(File.Exists(path + ".meta"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CameraTextureAsset_Resize_UpdatesAssetFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VerityTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Camera.rendertexture");

        try
        {
            new CameraTextureAssetData().Save(path);
            var asset = new CameraTextureAsset(path);

            asset.Resize(64, 32);
            var loaded = asset.LoadSettings();

            Assert.Equal(64, loaded.Width);
            Assert.Equal(32, loaded.Height);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
