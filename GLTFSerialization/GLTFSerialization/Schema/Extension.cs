using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GLTF.Extensions;

namespace GLTF.Schema
{
	public interface Extension
	{
		void Serialize(JsonWriter writer);
	}

	public abstract class ExtensionFactory
	{
		public string ExtensionName;
		public abstract Extension Deserialize(GLTFRoot root, JsonReader extensionToken);
	}

	public class DefaultExtension : Extension
	{
		public JProperty ExtensionData { get; internal set; }

		public void Serialize(JsonWriter writer)
		{ 
		}
	}

	public class DefaultExtensionFactory : ExtensionFactory
	{
		public override Extension Deserialize(GLTFRoot root, JsonReader extensionToken)
		{
			return new DefaultExtension
			{};
		}
	}
}
