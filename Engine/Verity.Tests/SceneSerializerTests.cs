using Verity.Core.Serialization;
using Verity.Core.World;

namespace Verity.Tests;

public class SceneSerializerTests
{
    [Fact]
    public void SerializeDeserialize_PreservesEntityTag()
    {
        var world = new World("Test");
        var entity = world.CreateEntity("Tagged");
        entity.Tag = "MainCamera";

        string json = SceneSerializer.Serialize(world);
        var loaded = new World("Loaded");
        SceneSerializer.Deserialize(loaded, json);

        Assert.Equal("MainCamera", loaded.RootEntities[0].Tag);
    }
}
