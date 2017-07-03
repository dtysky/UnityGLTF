using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Text.RegularExpressions;
using System;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using System.IO;
using UnityEditor;

namespace GLTF
{
	public class GLTFFileLoader
	{
		public enum MaterialType
		{
			PbrMetallicRoughness,
			PbrSpecularGlossiness,
			CommonConstant,
			CommonPhong,
			CommonBlinn,
			CommonLambert
		}

		public bool Multithreaded = true;
		public int MaximumLod = 300;
		protected readonly bool _saveInProject;
		protected readonly string _gltfUrl;
		protected readonly string _prefabName;
		protected readonly string _importDirectory;
		protected readonly string _importTexturesDirectory;
		protected readonly string _importMeshesDirectory;
		protected readonly string _importMaterialsDirectory;
		protected GLTFRoot _root;
		protected GameObject _lastLoadedScene;
		protected AsyncAction asyncAction;
		protected readonly Transform _sceneParent;

		protected readonly Material DefaultMaterial = new Material();
		protected readonly Dictionary<MaterialType, Shader> _shaderCache = new Dictionary<MaterialType, Shader>();

		public string getPathProjectFromAbsolute(string absolutePath)
		{
			return absolutePath.Replace(Application.dataPath, "Assets");
		}

		public string getPathAbsoluteFromProject(string projectPath)
		{
			return projectPath.Replace("Assets/", Application.dataPath);
		}

		public GLTFFileLoader(string gltfUrl, Transform parent = null, bool saveInProject = false, string importDirectory = "")
		{
			_gltfUrl = gltfUrl;
			_saveInProject = saveInProject;
			_importDirectory = importDirectory;
			if(_saveInProject && !Directory.Exists(importDirectory))
			{
				Debug.Log("Error: import directory '" + _importDirectory + "' doesn't exist. Aborting");
				return;
			}
			if (_saveInProject)
			{
				_importTexturesDirectory = _importDirectory + "/textures";
				Directory.CreateDirectory(_importTexturesDirectory);

				_importMeshesDirectory = _importDirectory + "/meshes";
				Directory.CreateDirectory(_importMeshesDirectory);

				_importMaterialsDirectory = _importDirectory + "/materials";
				Directory.CreateDirectory(_importMaterialsDirectory);

				_prefabName = Path.GetFileNameWithoutExtension(_gltfUrl);
			}

			_sceneParent = parent;
			asyncAction = new AsyncAction();
		}

		public GameObject LastLoadedScene
		{
			get
			{
				return _lastLoadedScene;
			}
		}

		public virtual void SetShaderForMaterialType(MaterialType type, Shader shader)
		{
			_shaderCache.Add(type, shader);
		}

		public virtual IEnumerator Load(int sceneIndex = -1)
		{
			byte[] gltfData = File.ReadAllBytes(_gltfUrl);
			ParseGLTF(gltfData);

			Scene scene;
			if (sceneIndex >= 0 && sceneIndex < _root.Scenes.Count)
			{
				scene = _root.Scenes[sceneIndex];
			}
			else
			{
				scene = _root.GetDefaultScene();
			}

			if (scene == null)
			{
				throw new Exception("No default scene in gltf file.");
			}

			if (_lastLoadedScene == null)
			{
				if (_root.Buffers != null)
				{
					foreach (var buffer in _root.Buffers)
					{
						yield return LoadBuffer(buffer);
					}
				}

				if (_root.Images != null)
				{
					foreach (var image in _root.Images)
					{
						yield return LoadImage(image);
					}
				}

				// generate these in advance instead of as-needed
				if (Multithreaded)
				{
					yield return asyncAction.RunOnWorkerThread(() => BuildMeshAttributes());
				}
			}

			var sceneObj = CreateScene(scene);
			if (_saveInProject)
			{
				string prefabPathInProject = getPathProjectFromAbsolute(_importDirectory);
				UnityEngine.Object prefab = PrefabUtility.CreateEmptyPrefab(prefabPathInProject + "/" + _prefabName + ".prefab");
				PrefabUtility.ReplacePrefab(sceneObj, prefab, ReplacePrefabOptions.ConnectToPrefab);
				Debug.Log("Prefab created!");
			}

			if (_sceneParent != null)
			{
				sceneObj.transform.SetParent(_sceneParent, false);
			}

			_lastLoadedScene = sceneObj;
		}

		protected virtual void ParseGLTF(byte[] gltfData)
		{
			byte[] glbBuffer;
			_root = GLTFParser.ParseBinary(gltfData, out glbBuffer);

			if (glbBuffer != null)
			{
				_root.Buffers[0].Contents = glbBuffer;
			}
		}

		protected virtual void BuildMeshAttributes()
		{
			foreach (var mesh in _root.Meshes)
			{
				foreach (var primitive in mesh.Primitives)
				{
					primitive.BuildMeshAttributes();
				}
			}
		}

		protected virtual GameObject CreateScene(Scene scene)
		{
			var sceneObj = new GameObject(scene.Name ?? "GLTFScene");

			foreach (var node in scene.Nodes)
			{
				var nodeObj = CreateNode(node.Value);
				nodeObj.transform.SetParent(sceneObj.transform, false);
			}

			return sceneObj;
		}

		protected virtual GameObject CreateNode(Node node)
		{
			var nodeObj = new GameObject(node.Name ?? "GLTFNode");

			Vector3 position;
			Quaternion rotation;
			Vector3 scale;
			node.GetUnityTRSProperties(out position, out rotation, out scale);
			nodeObj.transform.localPosition = position;
			nodeObj.transform.localRotation = rotation;
			nodeObj.transform.localScale = scale;
			
			// TODO: Add support for skin/morph targets
			if (node.Mesh != null)
			{
				CreateMeshObject(node.Mesh.Value, nodeObj.transform, node.Name);
			}

			/* TODO: implement camera (probably a flag to disable for VR as well)
			if (camera != null)
			{
				GameObject cameraObj = camera.Value.Create();
				cameraObj.transform.parent = nodeObj.transform;
			}
			*/

			if (node.Children != null)
			{
				foreach (var child in node.Children)
				{
					var childObj = CreateNode(child.Value);
					childObj.transform.SetParent(nodeObj.transform, false);
				}
			}

			return nodeObj;
		}

		protected virtual void CreateMeshObject(Mesh mesh, Transform parent, string objectName = "")
		{
			foreach (var primitive in mesh.Primitives)
			{
				var primitiveObj = CreateMeshPrimitive(primitive, objectName);
				primitiveObj.transform.SetParent(parent, false);
				primitiveObj.SetActive(true);
			}
		}

		protected virtual GameObject CreateMeshPrimitive(MeshPrimitive primitive, string objectName = "")
		{
			var primitiveObj = new GameObject(objectName.Length > 0 ? objectName : "Primitive");

			var meshFilter = primitiveObj.AddComponent<MeshFilter>();
			var vertexCount = primitive.Attributes[SemanticProperties.POSITION].Value.Count;

			if (primitive.Contents == null)
			{
				primitive.Contents = new UnityEngine.Mesh
				{
					vertices = primitive.Attributes[SemanticProperties.POSITION].Value.AsVertexArray(),

					normals = primitive.Attributes.ContainsKey(SemanticProperties.NORMAL)
						? primitive.Attributes[SemanticProperties.NORMAL].Value.AsNormalArray()
						: null,

					uv = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(0))
						? primitive.Attributes[SemanticProperties.TexCoord(0)].Value.AsTexcoordArray()
						: null,

					uv2 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(1))
						? primitive.Attributes[SemanticProperties.TexCoord(1)].Value.AsTexcoordArray()
						: null,

					uv3 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(2))
						? primitive.Attributes[SemanticProperties.TexCoord(2)].Value.AsTexcoordArray()
						: null,

					uv4 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(3))
						? primitive.Attributes[SemanticProperties.TexCoord(3)].Value.AsTexcoordArray()
						: null,

					colors = primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
						? primitive.Attributes[SemanticProperties.Color(0)].Value.AsColorArray()
						: null,

					triangles = primitive.Indices != null
						? primitive.Indices.Value.AsTriangles()
						: MeshPrimitive.GenerateTriangles(vertexCount),

					tangents = primitive.Attributes.ContainsKey(SemanticProperties.TANGENT)
						? primitive.Attributes[SemanticProperties.TANGENT].Value.AsTangentArray()
						: null
				};
			}

			meshFilter.sharedMesh = saveMesh(primitive.Contents, objectName);

			var material = CreateUnityPBRMaterial(
				primitive.Material != null ? primitive.Material.Value : DefaultMaterial,
				primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
			);

			var meshRenderer = primitiveObj.AddComponent<MeshRenderer>();
			meshRenderer.material =saveMaterial(material);
			return primitiveObj;
		}

		private UnityEngine.Mesh saveMesh(UnityEngine.Mesh mesh, string objectName = "")
		{
			string name = objectName;
			string meshProjectPath = getPathProjectFromAbsolute(_importMeshesDirectory) + "/" + name + ".asset";

			AssetDatabase.CreateAsset(mesh, meshProjectPath);
			return (UnityEngine.Mesh)AssetDatabase.LoadAssetAtPath(meshProjectPath, typeof(UnityEngine.Mesh));
		}

		private UnityEngine.Material saveMaterial(UnityEngine.Material material)
		{
			string name = material.name.Length > 0? material.name : "default";
			string materialProjectPath = getPathProjectFromAbsolute(_importMaterialsDirectory) + "/" + name + ".mat";

			AssetDatabase.CreateAsset(material, materialProjectPath);
			return (UnityEngine.Material)AssetDatabase.LoadAssetAtPath(materialProjectPath, typeof(UnityEngine.Material));
		}

		private void saveStuff(UnityEngine.Object objt)
		{
			foreach (UnityEngine.Object obj in EditorUtility.CollectDependencies(new UnityEngine.Object[] { objt }))
			{
				Debug.Log(obj.GetType());
				if (obj.GetType() == typeof(UnityEngine.MeshRenderer))
				{
					MeshRenderer mr = obj as MeshRenderer;
					AssetDatabase.CreateAsset(mr.sharedMaterial, "Assets/material.mat");
					List<UnityEngine.Texture> allTexture = new List<UnityEngine.Texture>();
					Shader shader = mr.sharedMaterial.shader;
					for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
					{
						if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
						{
							UnityEngine.Texture2D texture = mr.sharedMaterial.GetTexture(ShaderUtil.GetPropertyName(shader, i)) as Texture2D;
							AssetDatabase.CreateAsset(texture, "Assets/texture" + i + ".texture");
						}
					}

				}
				else if (obj.GetType() == typeof(UnityEngine.MeshFilter))
				{
					MeshFilter mf = obj as MeshFilter;
					AssetDatabase.CreateAsset(mf.mesh, "Assets/mesh.mesh");
				}
				else if (obj.GetType() == typeof(UnityEngine.GameObject))
				{
					GameObject go = obj as GameObject;
				}
			}
		}

		private bool getPixelsFromTexture(ref Texture2D texture, out Color[] pixels)
		{
			//Make texture readable
			TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
			if (!im)
			{
				pixels = new Color[1];
				return false;
			}

			bool readable = im.isReadable;
			TextureImporterCompression format = im.textureCompression;
			TextureImporterType type = im.textureType;
			bool isConvertedBump = im.convertToNormalmap;

			if (!readable)
				im.isReadable = true;
			if (type != TextureImporterType.Default)
				im.textureType = TextureImporterType.Default;

			im.textureCompression = TextureImporterCompression.Uncompressed;
			im.SaveAndReimport();

			pixels = texture.GetPixels();


			if (!readable)
				im.isReadable = false;
			if (type != TextureImporterType.Default)
				im.textureType = type;

			if (isConvertedBump)
				im.convertToNormalmap = true;

			im.textureCompression = format;
			im.SaveAndReimport();

			return true;
		}

		private List<UnityEngine.Texture2D> splitMetalRoughTexture(Texture2D inputTexture)
		{
			List<UnityEngine.Texture2D> outputs = new List<UnityEngine.Texture2D>();
			int width = inputTexture.width;
			int height = inputTexture.height;

			Color[] occlusion = new Color[width * height];
			Color[] metalRough = new Color[width * height];
			Color[] textureColors = new Color[inputTexture.width * inputTexture.height];

			Debug.Log(getPixelsFromTexture(ref inputTexture, out textureColors));
			Debug.Log(textureColors[3]);
			for (int i = 0; i < height; ++i)
			{
				for (int j = 0; j < width; ++j)
				{
					occlusion[i * width + j] = new Color(textureColors[i * width + j].r, textureColors[i * width + j].r, textureColors[i * width + j].r, 1.0f);
					metalRough[i * width + j] = new Color(textureColors[i * width + j].b, textureColors[i * width + j].b, textureColors[i * width + j].b, 1.0f - textureColors[i * width + j].g);
				}
			}

			Texture2D metalRoughTexture = new Texture2D(width, height);
			metalRoughTexture.SetPixels(metalRough);

			Texture2D occlusionTexture = new Texture2D(width, height);
			occlusionTexture.SetPixels(occlusion);

			//write textures
			string inputTexturePath = AssetDatabase.GetAssetPath(inputTexture);
			string metalRoughPath = Path.GetDirectoryName(inputTexturePath) + "/" + Path.GetFileNameWithoutExtension(inputTexturePath) + "_metal" + Path.GetExtension(inputTexturePath);
			string occlusionPath = Path.GetDirectoryName(inputTexturePath) + "/" + Path.GetFileNameWithoutExtension(inputTexturePath) + "_occlusion" + Path.GetExtension(inputTexturePath);

			File.WriteAllBytes(metalRoughPath, metalRoughTexture.EncodeToPNG());
			File.WriteAllBytes(occlusionPath, occlusionTexture.EncodeToPNG());

			AssetDatabase.ImportAsset(metalRoughPath);
			AssetDatabase.ImportAsset(occlusionPath);

			outputs.Add(metalRoughTexture);
			outputs.Add(occlusionTexture);
		    return outputs;
		}

		protected virtual UnityEngine.Material CreateUnityPBRMaterial(Material def, bool useVertexColors)
		{
			if (def.ContentsWithVC == null || def.ContentsWithoutVC == null)
			{
				Shader shader = Shader.Find("Standard");

				shader.maximumLOD = MaximumLod;

				var material = new UnityEngine.Material(shader);
				material.name = def.Name;

				//if (def.AlphaMode == AlphaMode.MASK)
				//{
				//	material.SetOverrideTag("RenderType", "TransparentCutout");
				//	material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				//	material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				//	material.SetInt("_ZWrite", 1);
				//	material.EnableKeyword("_ALPHATEST_ON");
				//	material.DisableKeyword("_ALPHABLEND_ON");
				//	material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				//	material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				//	material.SetFloat("_Cutoff", (float)def.AlphaCutoff);
				//}
				//else if (def.AlphaMode == AlphaMode.BLEND)
				//{
				//	material.SetOverrideTag("RenderType", "Transparent");
				//	material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
				//	material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				//	material.SetInt("_ZWrite", 0);
				//	material.DisableKeyword("_ALPHATEST_ON");
				//	material.EnableKeyword("_ALPHABLEND_ON");
				//	material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				//	material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				//}
				//else
				//{
				//	material.SetOverrideTag("RenderType", "Opaque");
				//	material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				//	material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				//	material.SetInt("_ZWrite", 1);
				//	material.DisableKeyword("_ALPHATEST_ON");
				//	material.DisableKeyword("_ALPHABLEND_ON");
				//	material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				//	material.renderQueue = -1;
				//}

				//if (def.DoubleSided)
				//{
				//	material.SetInt("_Cull", (int)CullMode.Off);
				//}
				//else
				//{
				//	material.SetInt("_Cull", (int)CullMode.Back);
				//}

				if (def.PbrMetallicRoughness != null)
				{
					var pbr = def.PbrMetallicRoughness;

					material.SetColor("_Color", pbr.BaseColorFactor);

					if (pbr.BaseColorTexture != null)
					{
						var texture = pbr.BaseColorTexture.Index.Value;
						material.SetTexture("_MainTex", CreateTexture(texture));
					}
					material.EnableKeyword("_METALLICGLOSSMAP");
					material.SetFloat("_Metallic", (float)pbr.MetallicFactor);
					material.SetFloat("_Glossiness", 1.0f - (float)pbr.RoughnessFactor);

					if (pbr.MetallicRoughnessTexture != null)
					{
						material.EnableKeyword("_METALLICGLOSSMAP");

						var texture = pbr.MetallicRoughnessTexture.Index.Value;
						UnityEngine.Texture2D inputTexture = CreateTexture(texture) as Texture2D;
						List<Texture2D> splitTextures = splitMetalRoughTexture(inputTexture);
						material.SetTexture("_MetallicGlossMap", splitTextures[0]);
						material.EnableKeyword("_METALLICGLOSSMAP");

						if (def.OcclusionTexture != null)
						{
							material.SetFloat("_OcclusionStrength", (float)def.OcclusionTexture.Strength);
							material.SetTexture("_OcclusionMap", splitTextures[1]);
						}
					}
				}

				//if (def.CommonConstant != null)
				//{
				//	material.SetColor("_AmbientFactor", def.CommonConstant.AmbientFactor);

				//	if (def.CommonConstant.LightmapTexture != null)
				//	{
				//		material.EnableKeyword("LIGHTMAP_ON");

				//		var texture = def.CommonConstant.LightmapTexture.Index.Value;
				//		material.SetTexture("_LightMap", CreateTexture(texture));
				//		material.SetInt("_LightUV", def.CommonConstant.LightmapTexture.TexCoord);
				//	}

				//	material.SetColor("_LightFactor", def.CommonConstant.LightmapFactor);
				//}

				if (def.NormalTexture != null)
				{
					var texture = def.NormalTexture.Index.Value;
					material.SetTexture("_BumpMap", CreateTexture(texture));
					material.SetFloat("_BumpScale", (float)def.NormalTexture.Scale);
				}

				if (def.EmissiveTexture != null)
				{
					var texture = def.EmissiveTexture.Index.Value;
					material.EnableKeyword("EMISSION_MAP_ON");
					material.SetTexture("_EmissionMap", CreateTexture(texture));
					material.SetInt("_EmissionUV", def.EmissiveTexture.TexCoord);
				}

				material.SetColor("_EmissionColor", def.EmissiveFactor);

				def.ContentsWithoutVC = material;
				def.ContentsWithVC = new UnityEngine.Material(material);
				def.ContentsWithVC.EnableKeyword("VERTEX_COLOR_ON");
			}

			return def.GetContents(useVertexColors);
		}


		protected virtual UnityEngine.Material CreateMaterial(Material def, bool useVertexColors)
		{
			if (def.ContentsWithVC == null || def.ContentsWithoutVC == null)
			{
				Shader shader;

				// get the shader to use for this material
				try
				{
					if (def.PbrMetallicRoughness != null)
						shader = _shaderCache[MaterialType.PbrMetallicRoughness];
					else if (_root.ExtensionsUsed != null && _root.ExtensionsUsed.Contains("KHR_materials_common")
					         && def.CommonConstant != null)
						shader = _shaderCache[MaterialType.CommonConstant];
					else
						shader = _shaderCache[MaterialType.PbrMetallicRoughness];
				}
				catch (KeyNotFoundException e)
				{
					Debug.LogWarningFormat("No shader supplied for type of glTF material {0}, using Standard fallback", def.Name);
					shader = Shader.Find("Standard");
				}

				shader.maximumLOD = MaximumLod;

				var material = new UnityEngine.Material(shader);
				material.name = def.Name;

				if (def.AlphaMode == AlphaMode.MASK)
				{
					material.SetOverrideTag("RenderType", "TransparentCutout");
					material.SetInt("_SrcBlend", (int) UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int) UnityEngine.Rendering.BlendMode.Zero);
					material.SetInt("_ZWrite", 1);
					material.EnableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.AlphaTest;
					material.SetFloat("_Cutoff", (float) def.AlphaCutoff);
				}
				else if (def.AlphaMode == AlphaMode.BLEND)
				{
					material.SetOverrideTag("RenderType", "Transparent");
					material.SetInt("_SrcBlend", (int) UnityEngine.Rendering.BlendMode.SrcAlpha);
					material.SetInt("_DstBlend", (int) UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					material.SetInt("_ZWrite", 0);
					material.DisableKeyword("_ALPHATEST_ON");
					material.EnableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
				}
				else
				{
					material.SetOverrideTag("RenderType", "Opaque");
					material.SetInt("_SrcBlend", (int) UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int) UnityEngine.Rendering.BlendMode.Zero);
					material.SetInt("_ZWrite", 1);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = -1;
				}

				if (def.DoubleSided)
				{
					material.SetInt("_Cull", (int) CullMode.Off);
				}
				else
				{
					material.SetInt("_Cull", (int) CullMode.Back);
				}

				if (def.PbrMetallicRoughness != null)
				{
					var pbr = def.PbrMetallicRoughness;

					material.SetColor("_Color", pbr.BaseColorFactor);

					if (pbr.BaseColorTexture != null)
					{
						var texture = pbr.BaseColorTexture.Index.Value;
						material.SetTexture("_MainTex", CreateTexture(texture));
					}

					material.SetFloat("_Metallic", (float) pbr.MetallicFactor);

					if (pbr.MetallicRoughnessTexture != null)
					{
						var texture = pbr.MetallicRoughnessTexture.Index.Value;
						material.SetTexture("_MetallicRoughnessMap", CreateTexture(texture));
					}

					material.SetFloat("_Roughness", (float) pbr.RoughnessFactor);
				}

				if (def.CommonConstant != null)
				{
					material.SetColor("_AmbientFactor", def.CommonConstant.AmbientFactor);

					if (def.CommonConstant.LightmapTexture != null)
					{
						material.EnableKeyword("LIGHTMAP_ON");

						var texture = def.CommonConstant.LightmapTexture.Index.Value;
						material.SetTexture("_LightMap", CreateTexture(texture));
						material.SetInt("_LightUV", def.CommonConstant.LightmapTexture.TexCoord);
					}

					material.SetColor("_LightFactor", def.CommonConstant.LightmapFactor);
				}

				if (def.NormalTexture != null)
				{
					var texture = def.NormalTexture.Index.Value;
					material.SetTexture("_BumpMap", CreateTexture(texture));
					material.SetFloat("_BumpScale", (float) def.NormalTexture.Scale);
				}

				if (def.OcclusionTexture != null)
				{
					var texture = def.OcclusionTexture.Index;

					material.SetFloat("_OcclusionStrength", (float) def.OcclusionTexture.Strength);

					if (def.PbrMetallicRoughness != null
					    && def.PbrMetallicRoughness.MetallicRoughnessTexture != null
					    && def.PbrMetallicRoughness.MetallicRoughnessTexture.Index.Id == texture.Id)
					{
						material.EnableKeyword("OCC_METAL_ROUGH_ON");
					}
					else
					{
						material.SetTexture("_OcclusionMap", CreateTexture(texture.Value));
					}
				}

				if (def.EmissiveTexture != null)
				{
					var texture = def.EmissiveTexture.Index.Value;
					material.EnableKeyword("EMISSION_MAP_ON");
					material.SetTexture("_EmissionMap", CreateTexture(texture));
					material.SetInt("_EmissionUV", def.EmissiveTexture.TexCoord);
				}

				material.SetColor("_EmissionColor", def.EmissiveFactor);
			
				def.ContentsWithoutVC = material;
				def.ContentsWithVC = new UnityEngine.Material(material);
				def.ContentsWithVC.EnableKeyword("VERTEX_COLOR_ON");
			}

			return def.GetContents(useVertexColors);
		}

		protected virtual UnityEngine.Texture CreateTexture(Texture texture)
		{
			if (texture.Contents)
				return texture.Contents;

			var source = texture.Source.Value.Contents;
			var desiredFilterMode = FilterMode.Bilinear;
			var desiredWrapMode = UnityEngine.TextureWrapMode.Repeat;

			if (texture.Sampler != null)
			{
				var sampler = texture.Sampler.Value;
				switch (sampler.MinFilter)
				{
					case MinFilterMode.Nearest:
						desiredFilterMode = FilterMode.Point;
						break;
					case MinFilterMode.Linear:
					default:
						desiredFilterMode = FilterMode.Bilinear;
						break;
				}

				switch (sampler.WrapS)
				{
					case GLTF.WrapMode.ClampToEdge:
						desiredWrapMode = UnityEngine.TextureWrapMode.Clamp;
						break;
					case GLTF.WrapMode.Repeat:
					default:
						desiredWrapMode = UnityEngine.TextureWrapMode.Repeat;
						break;
				}
			}

			if (source.filterMode == desiredFilterMode && source.wrapMode == desiredWrapMode)
			{
				texture.Contents = source;
			}
			else
			{
				texture.Contents = UnityEngine.Object.Instantiate(source);
				texture.Contents.filterMode = desiredFilterMode;
				texture.Contents.wrapMode = desiredWrapMode;
			}

			return texture.Contents;
		}

		protected const string Base64StringInitializer = "^data:[a-z-]+/[a-z-]+;base64,";

		/// <summary>
		///  Get the absolute path to a gltf uri reference.
		/// </summary>
		/// <param name="relativePath">The relative path stored in the uri.</param>
		/// <returns></returns>
		protected virtual string AbsolutePath(string relativePath)
		{
			var uri = new Uri(_gltfUrl);
			var partialPath = uri.AbsoluteUri.Remove(uri.AbsoluteUri.Length - uri.Segments[uri.Segments.Length - 1].Length);
			return partialPath + relativePath;
		}

		private Texture2D flipYtexture(Texture2D inputTexture)
		{
			int width = inputTexture.width;
			int height = inputTexture.height;
			Color[] inputPixels = new Color[width * height];
			Color[] outputPixels = new Color[width * height];
			getPixelsFromTexture(ref inputTexture, out inputPixels);

			for (int i = 0; i < height; ++i)
			{
				for (int j = 0; j < width; ++j)
				{
					outputPixels[i * width + j] = inputPixels[(height - i - 1) * width + j];
				}
			}

			Texture2D output = new Texture2D(width, height);
			output.SetPixels(outputPixels);
			string outputPath = AssetDatabase.GetAssetPath(inputTexture);
			File.WriteAllBytes(outputPath, output.EncodeToPNG());

			AssetDatabase.ImportAsset(outputPath);
			output = (Texture2D)AssetDatabase.LoadAssetAtPath(outputPath, typeof(Texture2D));

			return output;
		}

		protected virtual IEnumerator LoadImage(Image image, bool flipY = true)
		{
			Texture2D texture = null;
			if(_saveInProject)
			{
				if(image.Uri == null )
				{
					Debug.Log("Image file '" + image.Uri + "' doesn't exist");
				}

				// uri paths are relative to glTF file
				string gltfDirectory = Path.GetDirectoryName(_gltfUrl);
				string inputTexturePath = Path.Combine(gltfDirectory, image.Uri);
				if (!File.Exists(inputTexturePath))
				{
					Debug.LogError("The texture '" + inputTexturePath + "' doesn't exist.");
				}
				string textureFilename = Path.GetFileName(inputTexturePath);
				string textureOutput = _importTexturesDirectory + "/" + textureFilename;

    			if (!File.Exists(textureOutput)) 
					File.Copy(inputTexturePath, textureOutput);

				string outputTextureProjectPath = getPathProjectFromAbsolute(textureOutput);

				Debug.Log("Importing asset '" + textureOutput + "' ...");
				AssetDatabase.ImportAsset(outputTextureProjectPath);

				texture = (Texture2D)AssetDatabase.LoadAssetAtPath(outputTextureProjectPath, typeof(Texture2D));
				texture = flipYtexture(texture);

				if (!texture)
					Debug.Log("Failed to import texture asset '" + textureOutput + "'.");
			}
			else
			{
				var uri = image.Uri;
				if (image.Uri != null)
				{
					Regex regex = new Regex(Base64StringInitializer);
					Match match = regex.Match(uri);
					if (match.Success)
					{
						var base64Data = uri.Substring(match.Length);
						var textureData = Convert.FromBase64String(base64Data);
						texture = new Texture2D(0, 0);
						texture.LoadImage(textureData);
					}
					else
					{

						var www = UnityWebRequest.Get(AbsolutePath(uri));
						www.downloadHandler = new DownloadHandlerTexture();

						yield return www.Send();

						// HACK to enable mipmaps :(
						var tempTexture = DownloadHandlerTexture.GetContent(www);
						if (tempTexture != null)
						{
							texture = new Texture2D(tempTexture.width, tempTexture.height, tempTexture.format, true);
							texture.SetPixels(tempTexture.GetPixels());
							texture.Apply(true);
						}
						else
						{
							Debug.LogFormat("{0} {1}", www.responseCode, www.url);
							texture = new Texture2D(16, 16);
						}
					}
				}
				else
				{
					texture = new Texture2D(0, 0);
					var bufferView = image.BufferView.Value;
					var buffer = bufferView.Buffer.Value;
					var data = new byte[bufferView.ByteLength];
					System.Buffer.BlockCopy(buffer.Contents, bufferView.ByteOffset, data, 0, data.Length);
					texture.LoadImage(data);
				}
			}

			image.Contents = texture;
		}

		/// <summary>
		/// Load the remote URI data into a byte array.
		/// </summary>
		protected virtual IEnumerator LoadBuffer(Buffer buffer)
		{
			if (buffer.Uri != null)
			{
				byte[] bufferData;
				var uri = buffer.Uri;

				Regex regex = new Regex(Base64StringInitializer);
				Match match = regex.Match(uri);
				if (match.Success)
				{
					var base64Data = uri.Substring(match.Length);
					bufferData = Convert.FromBase64String(base64Data);
				}
				else
				{
					var www = UnityWebRequest.Get(AbsolutePath(uri));

					yield return www.Send();

					bufferData = www.downloadHandler.data;
				}

				buffer.Contents = bufferData;
			}
		}

		public virtual void Dispose()
		{
			foreach (var mesh in _root.Meshes)
			{
				foreach (var prim in mesh.Primitives)
				{
					GameObject.Destroy(prim.Contents);
				}
			}
		}

		private void convertMaterial(UnityEngine.Material mat)
		{
			
		}
	}
}
