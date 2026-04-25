using System.Text.Json.Serialization;
using Verity.Core;

namespace Verity.Core.Engine;

[JsonSerializable(typeof(BuildSettings))]
[JsonSerializable(typeof(ProjectSettings))]
[JsonSerializable(typeof(UiAssetReference))]
[JsonSerializable(typeof(UiRoleBinding))]
[JsonSerializable(typeof(EditorDockLayoutSettings))]
[JsonSerializable(typeof(UiAsset))]
public partial class CoreJsonContext : JsonSerializerContext
{
}
