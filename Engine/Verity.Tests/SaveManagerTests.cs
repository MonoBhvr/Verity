using System.Text.Json;
using Verity.Core.Serialization;

namespace Verity.Tests;

public sealed class SaveManagerTests : IDisposable
{
    private readonly string _saveDirectory = Path.Combine(Path.GetTempPath(), $"verity-save-tests-{Guid.NewGuid():N}");

    public SaveManagerTests()
    {
        SaveManager.SaveDirectory = _saveDirectory;

        if (Directory.Exists(_saveDirectory))
            Directory.Delete(_saveDirectory, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_saveDirectory))
            Directory.Delete(_saveDirectory, recursive: true);
    }

    [Fact]
    public void Save_And_Load_RoundTripsJsonData()
    {
        var data = new SaveData { Version = 2 };
        data.Set("playerName", "Aria");
        data.Set("level", 5);
        data.Set("isAlive", true);
        data.Set("stats", new Dictionary<string, object?>
        {
            ["hp"] = 42,
            ["mana"] = 9
        });
        data.Set("inventory", new List<object?> { "key", 3, false });

        SaveManager.Save(1, data);

        var saveFilePath = Path.Combine(_saveDirectory, "slot-1.json");
        var json = File.ReadAllText(saveFilePath);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("Version", out var versionElement));
        Assert.Equal(2, versionElement.GetInt32());

        var loaded = SaveManager.Load(1);

        Assert.Equal(2, loaded.Version);
        Assert.Equal("Aria", loaded.Get<string>("playerName"));
        Assert.Equal(5, loaded.Get<int>("level"));
        Assert.True(loaded.Get<bool>("isAlive"));

        var stats = loaded.Get<Dictionary<string, object?>>("stats");
        Assert.Equal(42, Convert.ToInt32(stats["hp"]));
        Assert.Equal(9, Convert.ToInt32(stats["mana"]));

        var inventory = loaded.Get<List<object?>>("inventory");
        Assert.Equal("key", inventory[0]);
        Assert.Equal(3, Convert.ToInt32(inventory[1]));
        Assert.False((bool)inventory[2]!);
    }

    [Fact]
    public void Save_Delete_And_GetUsedSlots_ManageSlots()
    {
        SaveManager.Save(3, new SaveData());
        SaveManager.Save(1, new SaveData());

        Assert.True(SaveManager.HasSave(1));
        Assert.True(SaveManager.HasSave(3));
        Assert.Equal([1, 3], SaveManager.GetUsedSlots());

        SaveManager.DeleteSave(1);

        Assert.False(SaveManager.HasSave(1));
        Assert.Equal([3], SaveManager.GetUsedSlots());
    }

    [Fact]
    public void Load_EmptySlot_ThrowsFileNotFoundException()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => SaveManager.Load(7));

        Assert.Contains("7", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LegacySaveWithoutVersion_DefaultsToVersionOne()
    {
        Directory.CreateDirectory(_saveDirectory);
        File.WriteAllText(
            Path.Combine(_saveDirectory, "slot-4.json"),
            """
            {
              "Data": {
                "coins": 99,
                "playerName": "Legacy"
              }
            }
            """);

        var loaded = SaveManager.Load(4);

        Assert.Equal(1, loaded.Version);
        Assert.Equal(99, loaded.Get<int>("coins"));
        Assert.Equal("Legacy", loaded.Get<string>("playerName"));
    }
}
