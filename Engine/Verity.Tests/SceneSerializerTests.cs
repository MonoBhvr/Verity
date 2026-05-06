using System.Text.Json.Nodes;
using Verity.Core.Serialization;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Tests;

public class SceneSerializerTests
{
    public sealed class EntityReferenceProbe : Component
    {
        public Entity? Target { get; set; }
    }

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

    [Fact]
    public void SerializeDeserialize_PreservesEntityReference()
    {
        var world = new World("Test");
        var owner = world.CreateEntity("Owner");
        var target = world.CreateEntity("Target");
        owner.AddComponent<EntityReferenceProbe>().Target = target;

        string json = SceneSerializer.Serialize(world);
        var loaded = new World("Loaded");
        SceneSerializer.Deserialize(loaded, json, typeof(EntityReferenceProbe).Assembly);

        var loadedOwner = loaded.GetAllEntities().First(e => e.Name == "Owner");
        var loadedTarget = loaded.GetAllEntities().First(e => e.Name == "Target");
        Assert.Same(loadedTarget, loadedOwner.GetComponent<EntityReferenceProbe>()!.Target);
    }

    [Fact]
    public void SerializeDeserialize_PreservesForwardEntityReference()
    {
        var world = new World("Test");
        var owner = world.CreateEntity("Owner");
        var target = world.CreateEntity("Target");
        owner.AddComponent<EntityReferenceProbe>().Target = target;

        string json = SceneSerializer.Serialize(world);
        var loaded = new World("Loaded");
        SceneSerializer.Deserialize(loaded, json, typeof(EntityReferenceProbe).Assembly);

        var loadedOwner = loaded.RootEntities[0];
        var loadedTarget = loaded.RootEntities[1];
        Assert.Same(loadedTarget, loadedOwner.GetComponent<EntityReferenceProbe>()!.Target);
    }

    [Fact]
    public void InstantiateBlueprintInstance_RetargetsEntityReferenceToClone()
    {
        var blueprintWorld = new World("Blueprint");
        var owner = blueprintWorld.CreateEntity("Owner");
        var target = blueprintWorld.CreateEntity("Target");
        target.Transform.SetParent(owner.Transform, false);
        owner.AddComponent<EntityReferenceProbe>().Target = target;

        string path = Path.Combine(Path.GetTempPath(), $"verity-blueprint-{Guid.NewGuid():N}.blueprint");
        File.WriteAllText(path, SceneSerializer.SerializeEntity(owner));
        try
        {
            var world = new World("Loaded");
            Entity cloneRoot = SceneSerializer.InstantiateBlueprintInstance(world, path, typeof(EntityReferenceProbe).Assembly)!;

            Entity cloneTarget = cloneRoot.Transform.Children[0].Owner;
            Assert.Same(cloneTarget, cloneRoot.GetComponent<EntityReferenceProbe>()!.Target);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SerializeDeserialize_PreservesBlueprintAssetEntityReference()
    {
        var world = new World("Test");
        var owner = world.CreateEntity("Owner");
        owner.AddComponent<EntityReferenceProbe>().Target = new Entity("Player")
        {
            BlueprintAssetPath = "Assets/Player.blueprint",
            BlueprintAssetGuid = "blueprint-guid"
        };

        string json = SceneSerializer.Serialize(world);
        JsonObject targetJson = JsonNode.Parse(json)!["Entities"]![0]!["Components"]![0]!["Fields"]!["Target"]!.AsObject();
        Assert.NotNull(targetJson["BlueprintAsset"]);
        Assert.Null(targetJson["EntityId"]);

        var loaded = new World("Loaded");
        SceneSerializer.Deserialize(loaded, json, typeof(EntityReferenceProbe).Assembly);

        Entity? target = loaded.RootEntities[0].GetComponent<EntityReferenceProbe>()!.Target;
        Assert.NotNull(target);
        Assert.Equal("Assets/Player.blueprint", target.BlueprintAssetPath);
        Assert.Equal("blueprint-guid", target.BlueprintAssetGuid);
        Assert.Single(loaded.GetAllEntities());
    }
}
