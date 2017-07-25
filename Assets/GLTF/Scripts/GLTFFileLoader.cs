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

		Dictionary<int, UnityEngine.Object> _registeredMaterials;

		public string currentStatus = "";

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

		public string getStatus()
		{
			return currentStatus;
		}

		public GLTFFileLoader(string gltfUrl, Transform parent = null, bool saveInProject = false, string importDirectory = "")
		{
			_registeredMaterials = new Dictionary<int, UnityEngine.Object>();
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
			currentStatus = "Initializing...";
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

		public virtual void Load(int sceneIndex = -1)
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
						LoadBuffer(buffer);
					}
				}

				if (_root.Images != null)
				{
					foreach (var image in _root.Images)
					{
						LoadImage(image);
					}
				}

				// generate these in advance instead of as-needed
				if (Multithreaded)
				{
					asyncAction.RunOnWorkerThread(() => BuildMeshAttributes());
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

			for(int i=0; i< scene.Nodes.Count; ++i)
			{
				var node = scene.Nodes[i];
				var nodeObj = CreateNode(node.Value, i);
				nodeObj.transform.SetParent(sceneObj.transform, false);
			}

			return sceneObj;
		}

		protected virtual GameObject CreateNode(Node node, int index)
		{
			var nodeObj = new GameObject(node.Name.Length > 0 ? node.Name : "GLTFNode_" + index);

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
				CreateMeshObject(node.Mesh.Value, nodeObj.transform, nodeObj.name);
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
					int ind = _root.Nodes.IndexOf(child.Value);
					var childObj = CreateNode(child.Value, ind);
					childObj.transform.SetParent(nodeObj.transform, false);
				}
			}

			return nodeObj;
		}

		protected virtual void CreateMeshObject(Mesh mesh, Transform parent, string objectName = "")
		{	
			for(int i=0; i < mesh.Primitives.Count; ++i)
			{
				Debug.Log("Parsing primitive " + i);
				var primitive = mesh.Primitives[i];
				var primitiveObj = CreateMeshPrimitive(primitive, i, objectName);
				primitiveObj.transform.SetParent(parent, false);
				primitiveObj.SetActive(true);
			}
		}

		protected virtual GameObject CreateMeshPrimitive(MeshPrimitive primitive, int index, string objectName = "")
		{
			var primitiveObj = new GameObject(objectName.Length > 0 ? objectName : "Primitive " + index);

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
			Debug.Log("NB Materials  : " + _root.Materials.Count);
			meshFilter.sharedMesh = saveMesh(primitive.Contents, objectName + "_" +index );

			int materialIndex = _root.Materials.IndexOf(primitive.Material.Value);

			var material = _registeredMaterials.ContainsKey(materialIndex) ? (UnityEngine.Material)_registeredMaterials[materialIndex] : CreateUnityPBRMaterial(
				primitive.Material != null ? primitive.Material.Value : DefaultMaterial,
				primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
			);
			//var material = CreateMaterial(
			//	primitive.Material != null ? primitive.Material.Value : DefaultMaterial,
			//	primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
			//);

			var meshRenderer = primitiveObj.AddComponent<MeshRenderer>();
			meshRenderer.material = getOrCreateMaterialAsset(material, index);
			return primitiveObj;
		}

		private UnityEngine.Mesh saveMesh(UnityEngine.Mesh mesh, string objectName = "Scene")
		{
			if(!_registeredMaterials.ContainsKey(mesh.GetInstanceID()))
			{
				string meshProjectPath = getPathProjectFromAbsolute(_importMeshesDirectory) + "/" + objectName + "_" + _registeredMaterials.Count + ".asset";
				Debug.Log("Registering mesh " + meshProjectPath);
				AssetDatabase.CreateAsset(mesh, meshProjectPath);
				AssetDatabase.Refresh();
				_registeredMaterials.Add(mesh.GetInstanceID(), AssetDatabase.LoadAssetAtPath(meshProjectPath, typeof(UnityEngine.Mesh)));
			}

			return (UnityEngine.Mesh)_registeredMaterials[mesh.GetInstanceID()];
		}

		private UnityEngine.Material getOrCreateMaterialAsset(UnityEngine.Material material, int index)
		{
			if (!_registeredMaterials.ContainsKey(material.GetInstanceID()))
			{
				string name = material.name.Length > 0 ? material.name : "default";
				string materialProjectPath = getPathProjectFromAbsolute(_importMaterialsDirectory) + "/" + name + "_" + index + ".mat";
				if(!File.Exists(getPathAbsoluteFromProject(materialProjectPath)))
				{
					AssetDatabase.CreateAsset(material, materialProjectPath);
					AssetDatabase.Refresh();
				}
				
				
				UnityEngine.Material unityMaterial = (UnityEngine.Material)AssetDatabase.LoadAssetAtPath(materialProjectPath, typeof(UnityEngine.Material));
				_registeredMaterials.Add(material.GetInstanceID(), unityMaterial);
			}

			return (UnityEngine.Material)_registeredMaterials[material.GetInstanceID()];
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

		private List<UnityEngine.Texture2D> splitAndRemoveMetalRoughTexture(Texture2D inputTexture, bool hasOcclusion)
		{
			List<UnityEngine.Texture2D> outputs = new List<UnityEngine.Texture2D>();
			int width = inputTexture.width;
			int height = inputTexture.height;

			Color[] occlusion = new Color[width * height];
			Color[] metalRough = new Color[width * height];
			Color[] textureColors = new Color[width * height];

			getPixelsFromTexture(ref inputTexture, out textureColors);

			for (int i = 0; i < height; ++i)
			{
				for (int j = 0; j < width; ++j)
				{
					float occ = textureColors[i * width + j].r;
					float rough = textureColors[i * width + j].g;
					float met = textureColors[i * width + j].b;

					occlusion[i * width + j] = new Color(occ, occ, occ, 1.0f);
					metalRough[i * width + j] = new Color(met, met, met, 1.0f - rough);
				}
			}

			Texture2D metalRoughTexture = new Texture2D(width, height);
			metalRoughTexture.SetPixels(metalRough);
			metalRoughTexture.Apply();

			//write textures
			string inputTexturePath = AssetDatabase.GetAssetPath(inputTexture);
			string metalRoughPath = Path.GetDirectoryName(inputTexturePath) + "/" + Path.GetFileNameWithoutExtension(inputTexturePath) + "_metal" + Path.GetExtension(inputTexturePath);
			File.WriteAllBytes(metalRoughPath, metalRoughTexture.EncodeToPNG());
			AssetDatabase.Refresh();
			Texture2D metalTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(metalRoughPath, typeof(Texture2D));
			outputs.Add(metalTexture);
			Debug.Log(metalRoughPath);
			if (hasOcclusion)
			{
				Texture2D occlusionTexture = new Texture2D(width, height);
				occlusionTexture.SetPixels(occlusion);
				occlusionTexture.Apply();

				string occlusionPath = Path.GetDirectoryName(inputTexturePath) + "/" + Path.GetFileNameWithoutExtension(inputTexturePath) + "_occlusion" + Path.GetExtension(inputTexturePath);
				File.WriteAllBytes(occlusionPath, occlusionTexture.EncodeToPNG());
				AssetDatabase.Refresh();

				Texture2D occlusionTextureOutput = (Texture2D)AssetDatabase.LoadAssetAtPath(occlusionPath, typeof(Texture2D));
				outputs.Add(occlusionTextureOutput);
			}

			// Delete original texture
			AssetDatabase.DeleteAsset(inputTexturePath);
			AssetDatabase.Refresh();

		    return outputs;
		}

		protected virtual UnityEngine.Material CreateUnityPBRMaterial(Material def, bool useVertexColors)
		{
			if (def.ContentsWithVC == null || def.ContentsWithoutVC == null)
			{
				Shader shader = Shader.Find("Standard");

       			var material = new UnityEngine.Material(shader);
				material.name = def.Name;
				currentStatus = "Setting material '" + material.name + "'";
				material.EnableKeyword("_ALPHABLEND_ON");
				material.EnableKeyword("_ALPHATEST_ON");
				material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
				
				if (def.AlphaMode == AlphaMode.MASK)
				{
					
					material.SetFloat("_Mode", 1);
				}
				else if (def.AlphaMode == AlphaMode.BLEND)
				{
					
					material.SetFloat("_Mode", 3);
				}

				if (def.PbrMetallicRoughness != null)
				{
					material.EnableKeyword("_METALLICGLOSSMAP");

					var pbr = def.PbrMetallicRoughness;

					material.SetColor("_Color", pbr.BaseColorFactor);

					if (pbr.BaseColorTexture != null)
					{
						var texture = pbr.BaseColorTexture.Index.Value;
						material.SetTexture("_MainTex", CreateTexture(texture));
					}

					material.SetFloat("_Metallic", (float)pbr.MetallicFactor);
					material.SetFloat("_Glossiness", 1.0f - (float)pbr.RoughnessFactor);

					if (pbr.MetallicRoughnessTexture != null)
					{
						var texture = pbr.MetallicRoughnessTexture.Index.Value;
						UnityEngine.Texture2D inputTexture = CreateTexture(texture) as Texture2D;
						List<Texture2D> splitTextures = splitAndRemoveMetalRoughTexture(inputTexture, def.OcclusionTexture != null);
						if(splitTextures[0])
						{
							Debug.Log("METAL : " + AssetDatabase.GetAssetPath(splitTextures[0]));
							Debug.Log("OCCLUSION : " + AssetDatabase.GetAssetPath(splitTextures[0]));
						}
						else
						{
							Debug.Log("Textures not yet loaded");
						}
						material.SetTexture("_MetallicGlossMap", splitTextures[0]);

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
					material.EnableKeyword("_NORMALMAP");
					var texture = def.NormalTexture.Index.Value;
					Texture2D normalTexture = CreateTexture(texture) as Texture2D;
					// Automatically set it to normal map
					TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normalTexture)) as TextureImporter;
					im.textureType = TextureImporterType.NormalMap;
					im.SaveAndReimport();

					material.SetTexture("_BumpMap", CreateTexture(texture));
					material.SetFloat("_BumpScale", (float)def.NormalTexture.Scale);
				}

				material.EnableKeyword("_EMISSION");
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
			output.Apply();
			string outputPath = AssetDatabase.GetAssetPath(inputTexture);
			File.WriteAllBytes(outputPath, output.EncodeToPNG());

			AssetDatabase.ImportAsset(outputPath);
			output = (Texture2D)AssetDatabase.LoadAssetAtPath(outputPath, typeof(Texture2D));

			return output;
		}

		protected virtual void LoadImage(Image image, bool flipY = true)
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

				File.Copy(inputTexturePath, textureOutput);

				string outputTextureProjectPath = getPathProjectFromAbsolute(textureOutput);

				currentStatus = "Importing asset '" + textureOutput + "' ...";
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
					var base64Data = uri.Substring(match.Length);
					var textureData = Convert.FromBase64String(base64Data);
					texture = new Texture2D(0, 0);
					texture.LoadImage(textureData);
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
		protected virtual void LoadBuffer(Buffer buffer)
		{
			if (buffer.Uri != null)
			{
				byte[] bufferData;
				var uri = buffer.Uri;
				string absoluteBufferPath = Path.GetDirectoryName(_gltfUrl) + "/" + buffer.Uri;
				bufferData = File.ReadAllBytes(absoluteBufferPath);
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
