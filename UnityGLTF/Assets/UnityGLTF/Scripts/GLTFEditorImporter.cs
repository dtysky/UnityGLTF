using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GLTF;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityGLTF.Cache;
using UnityGLTF.Extensions;
using UnityEditor;

public enum ImporterState
{
	IDLE,
	IMPORT_IMAGES
}

namespace UnityGLTF
{
	/// <summary>
	/// Editor windows to load a GLTF scene in editor
	/// </summary>
	class GLTFEditorImporter : EditorWindow
	{
		[MenuItem("Tools/Import glTF %_e")]
		static void Init()
		{
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX // edit: added Platform Dependent Compilation - win or osx standalone
			GLTFEditorImporter window = (GLTFEditorImporter)EditorWindow.GetWindow(typeof(GLTFEditorImporter));
			window.titleContent.text = "glTF importer";
			window.Show();
#else // and error dialog if not standalone
			EditorUtility.DisplayDialog("Error", "Your build target must be set to standalone", "Okay");
#endif
			Initialize();
		}

		// Public
		public static Shader GLTFStandard;
		public static Shader GLTFConstant;
		public bool _useGLTFMaterial = false;

		private string _projectDirectoryPath;
		private string _gltfDirectoryPath;
		private string _glTFPath = "";
		private byte[] _glTFData;
		protected GLTFRoot _root;
		protected static  Dictionary<MaterialType, Shader> _shaderCache = new Dictionary<MaterialType, Shader>();

		protected AssetCache _assetCache;
		private static bool _isDone = true;
		AssetManager _assetSerializer = null;

		// UI
		static List<string> _messages;
		private static string _status = "";
		private static TaskManager _taskManager;

		//Debug only
		private static List<string> _importedFiles;
		private static int _nbParsedNodes;
		public UnityEngine.Material defaultMaterial;

		//Tests
		private static List<string> _samplesPathList;
		private static List<string> _samplesList;
		private int _currentSampleIndex = 0;
		private int _lastCurrentSampleIndex = -1;
		Vector3 _offset = new Vector3(0.1f, 0.0f, 0.0f);

		private string _defaultImportDirectory = "D:/Sketchfab/unityProjects/ImportGLTF/UnityGLTF/UnityGLTF/Assets/import";

		private static string GLTF_DIRECTORY = "D:/Samples/glTF/gltf";
		private static string GLTF_SPECULAR_DIRECTORY = "D:/Samples/glTF/gltfspecular";
		private static string SKFB_DIRECTORY = "D:/Samples/glTF/sketchfab";
		private static string _samplesDir = GLTF_SPECULAR_DIRECTORY;

		private static string _currentSampleName = "";

		public enum MaterialType
		{
			PbrMetallicRoughness,
			PbrSpecularGlossiness,
			CommonConstant,
			CommonPhong,
			CommonBlinn,
			CommonLambert
		}

		private static void setSamplesList(string samplesDirectory)
		{
			_samplesPathList = new List<string>();
			_samplesList = new List<string>();

			if(Directory.Exists(_samplesDir))
			{
				_samplesPathList.Clear();
				_samplesList.Clear();
				string[] samples = Directory.GetFiles(_samplesDir, "*.gltf", SearchOption.AllDirectories);
				foreach (string sample in samples)
				{
					_samplesPathList.Add(sample);
					_samplesList.Add(Path.GetFileNameWithoutExtension(sample));
				}
			}
		}

		private static void Initialize()
		{
			setSamplesList(SKFB_DIRECTORY);
			_messages = new List<string>();
			_importedFiles = new List<string>();
			_isDone = true;
			_taskManager = new TaskManager();

			// Should move
			GLTFStandard = Shader.Find("GLTF/GLTFStandard");
			GLTFConstant = Shader.Find("GLTF/GLTFConstant");
			Debug.Log(GLTFStandard);
			_shaderCache.Clear();
			_shaderCache.Add(GLTFEditorImporter.MaterialType.PbrMetallicRoughness, GLTFStandard);
			_shaderCache.Add(GLTFEditorImporter.MaterialType.CommonConstant, GLTFConstant);
		}
		private void setupForPath(string path)
		{
			//Setup
			_glTFPath = path;
			_gltfDirectoryPath = Path.GetDirectoryName(_glTFPath);

			string modeldir = Path.GetFileNameWithoutExtension(_glTFPath);
			_projectDirectoryPath = Path.Combine(_defaultImportDirectory, modeldir); //EditorUtility.OpenFolderPanel("Choose import directory in Project", Application.dataPath, "Samples");

			_currentSampleName = Path.GetFileNameWithoutExtension(_glTFPath);
			_messages.Clear();
			setStatus("Initialized");
		}

		private static void checkValidity()
		{
			if(_samplesList == null)
			{
				_samplesList = new List<string>();
			}

			if (_importedFiles == null)
			{
				_importedFiles = new List<string>();
			}
			if (_taskManager == null)
			{
				_taskManager = new TaskManager();
			}

			if(_messages == null)
			{
				_messages = new List<string>();
			}
			if(_samplesList.Count == 0)
				setSamplesList(GLTF_SPECULAR_DIRECTORY);
		}

		private void OnGUI()
		{
			checkValidity();

			// List of samples
			GUILayout.BeginHorizontal();
			GUILayout.Label("Import list from hardcoded directory: " + SKFB_DIRECTORY);
			_currentSampleIndex = EditorGUILayout.Popup(_currentSampleIndex, _samplesList.ToArray());
			if (_currentSampleIndex != _lastCurrentSampleIndex && _samplesList.Count > 0)
			{
				setupForPath(_samplesPathList[_currentSampleIndex]);
				_lastCurrentSampleIndex = _currentSampleIndex;
			}
			GUILayout.EndHorizontal();
			if(GUILayout.Button("Change directory"))
			{
				_samplesDir = EditorUtility.OpenFolderPanel("Choose glTF to import", _samplesDir, Application.dataPath);
				setSamplesList(_samplesDir);

				if(_samplesList.Count > 0)
					setupForPath(_samplesList[_currentSampleIndex]);
			}

			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			GUILayout.Label(" ----- or ----");
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			// Import popup
			if (GUILayout.Button("Import file from disk"))
			{
				_glTFPath = EditorUtility.OpenFilePanel("Choose glTF to import", Application.dataPath, "gltf");
				_gltfDirectoryPath = Path.GetDirectoryName(_glTFPath);

				string modeldir = Path.GetFileNameWithoutExtension(_glTFPath);
				_projectDirectoryPath = Path.Combine(_defaultImportDirectory, modeldir);
				Directory.CreateDirectory(_projectDirectoryPath);
			}

			GUILayout.Label("Paths");
			GUILayout.BeginVertical("Box");
			GUILayout.Label("Model to import: " + _glTFPath);
			GUILayout.Label("Import directory: " + _projectDirectoryPath);
			GUILayout.EndVertical();

			GUILayout.Label("Options");
			_useGLTFMaterial = GUILayout.Toggle(_useGLTFMaterial, "Use GLTF specific shader");

			GUI.enabled = _glTFPath.Length > 0 && File.Exists(_glTFPath);

			GUILayout.Label("");
			GUILayout.Label("");

			if (GUILayout.Button("IMPORT"))
			{
				Directory.CreateDirectory(_projectDirectoryPath);
				_assetSerializer = new AssetManager(_projectDirectoryPath, _glTFPath);
				Load();
			}
			GUI.enabled = true;

			GUILayout.Label("Status: " + _status);
		}

		public void Update()
		{
			// Updates
				if (_taskManager != null && _taskManager.play())
					Repaint();
				else
					_isDone = true;
		}

		public void OnDestroy()
		{
			//clean();
		}

		public static void setStatus(string status, bool last=false)
		{
			if(last)
			{
				_messages[_messages.Count - 1] = status;
			}
			else
			{
				_messages.Add(status);
			}

			_status = "";

			for (int i = 0; i < _messages.Count; ++i)
			{
				_status = _status + "\n" + _messages[i];
			}
		}

		private void Load()
		{
			_messages.Clear();
			LoadFile();
			LoadGLTFScene();
		}

		/// <summary>
		/// Load the remote URI data into a byte array.
		/// </summary>
		protected virtual void LoadBuffer(string sourceUri, GLTF.Schema.Buffer buffer, int bufferIndex)
		{
			if (buffer.Uri != null)
			{
				byte[] bufferData = null;
				var uri = buffer.Uri;
				var bufferPath = Path.Combine(sourceUri, uri);
				bufferData = File.ReadAllBytes(bufferPath);
				_assetCache.BufferCache[bufferIndex] = bufferData;
			}
		}

		private void clean()
		{
			foreach(string file in _importedFiles)
			{
				File.Delete(file);
			}

			AssetDatabase.Refresh();
		}

		private void LoadFile(int sceneIndex = -1)
		{
			_assetSerializer = new AssetManager(_projectDirectoryPath, _glTFPath);
			_glTFData = File.ReadAllBytes(_glTFPath);
			setStatus("Loaded file: " + _glTFPath);
			try
			{
				GLTFProperty.RegisterExtension(new KHR_materials_pbrSpecularGlossinessExtensionFactory());
			}catch(Exception)
			{
				Debug.Log("Already added");
			}

			_root = GLTFParser.ParseJson(_glTFData);
			setStatus("Parsed glTF data" + _root);
		}

		private void LoadGLTFScene(int sceneIndex = -1)
		{
			setStatus("Load scene");
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

			_assetCache = new AssetCache(
				_root.Images != null ? _root.Images.Count : 0,
				_root.Textures != null ? _root.Textures.Count : 0,
				_root.Materials != null ? _root.Materials.Count : 0,
				_root.Buffers != null ? _root.Buffers.Count : 0,
				_root.Meshes != null ? _root.Meshes.Count : 0
			);

			if(_root.Textures != null && _root.Images != null && _root.Textures.Count > _root.Images.Count)
			{
				Debug.LogError("More textures than images");
			}

			// Load dependencies
			LoadBuffersEnum();
			if(_root.Images != null)
				LoadImagesEnum();
			if (_root.Textures != null)
				LoadTexturesEnum();
			if(_root.Materials != null)
				LoadMaterialsEnum();
			LoadMeshesEnum();
			LoadSceneEnum();
		}

		private void LoadBuffersEnum()
		{
			_taskManager.addTask(LoadBuffers());
		}

		private IEnumerator LoadBuffers()
		{
			if (_root.Buffers != null)
			{
				// todo add fuzzing to verify that buffers are before uri
				for (int i = 0; i < _root.Buffers.Count; ++i)
				{
					GLTF.Schema.Buffer buffer = _root.Buffers[i];
					if (buffer.Uri != null)
					{
						LoadBuffer(_gltfDirectoryPath, buffer, i);
						setStatus("Loaded buffer from file " + buffer.Uri);
					}
					else //null buffer uri indicates GLB buffer loading
					{
						byte[] glbBuffer;
						GLTFParser.ExtractBinaryChunk(_glTFData, i, out glbBuffer);
						_assetCache.BufferCache[i] = glbBuffer;
						setStatus("Loaded embedded buffer " + i);
					}
					yield return null;
				}
			}
		}

		private void LoadImagesEnum()
		{
			_taskManager.addTask(LoadImages());
		}

		private IEnumerator LoadImages()
		{
			Debug.Log(_root.Images.Count);
			for (int i = 0; i < _root.Images.Count; ++i)
			{
				Image image = _root.Images[i];
				LoadImage(_gltfDirectoryPath, image, i);
				setStatus("Loaded Image " + (i + 1) + "/" + _root.Images.Count + (image.Uri != null ? "(" + image.Uri + ")" : " (embedded)"), i != 0);
				yield return null;
			}
		}

		protected const string Base64StringInitializer = "^data:[a-z-]+/[a-z-]+;base64,";

		private void LoadImage(string rootPath, Image image, int imageID)
		{
			if (_assetCache.ImageCache[imageID] == null)
			{
				if (image.Uri != null)
				{
					// Is base64 uri ?
					var uri = image.Uri;

					Regex regex = new Regex(Base64StringInitializer);
					Match match = regex.Match(uri);
					if (match.Success)
					{
						var base64Data = uri.Substring(match.Length);
						var textureData = Convert.FromBase64String(base64Data);

						_assetSerializer.registerImageFromData(textureData, imageID);
					}
					else if(File.Exists(Path.Combine(rootPath, uri))) // File is a real file
					{
						string imagePath = Path.Combine(rootPath, uri);
						_assetSerializer.copyAndRegisterImageInProject(imagePath, imageID);
					}
					else
					{
						Debug.Log("Image not found / Unknown image buffer");
					}
				}
				else
				{
					var bufferView = image.BufferView.Value;
					var buffer = bufferView.Buffer.Value;
					var data = new byte[bufferView.ByteLength];

					var bufferContents = _assetCache.BufferCache[bufferView.Buffer.Id];
					System.Buffer.BlockCopy(bufferContents, bufferView.ByteOffset, data, 0, data.Length);
					_assetSerializer.registerImageFromData(data, imageID);
				}
			}
		}

		private void LoadTexturesEnum()
		{
			_taskManager.addTask(LoadTextures());
		}

		private IEnumerator LoadTextures()
		{
			for(int i = 0; i < _root.Textures.Count; ++i)
			{
				SetupTexture(_root.Textures[i], i);
				setStatus("Loaded texture " + (i + 1) + "/" + _root.Textures.Count, i != 0);
				yield return null;
			}
		}

		private void SetupTexture(GLTF.Schema.Texture def, int textureIndex)
		{
			Texture2D source = _assetSerializer.getOrCreateTexture(def.Source.Id, textureIndex);

			// Default values
			var desiredFilterMode = FilterMode.Bilinear;
			var desiredWrapMode = UnityEngine.TextureWrapMode.Repeat;

			if (def.Sampler != null)
			{
				var sampler = def.Sampler.Value;
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
					case GLTF.Schema.WrapMode.ClampToEdge:
						desiredWrapMode = UnityEngine.TextureWrapMode.Clamp;
						break;
					case GLTF.Schema.WrapMode.Repeat:
					default:
						desiredWrapMode = UnityEngine.TextureWrapMode.Repeat;
						break;
				}
			}

			source.filterMode = desiredFilterMode;
			source.wrapMode = desiredWrapMode;
			_assetSerializer.registerTexture(source);
		}

		private void LoadSceneEnum()
		{
			_taskManager.addTask(LoadScene());
		}

		private void LoadMeshesEnum()
		{
			_taskManager.addTask(LoadMeshes());
		}

		private void LoadMaterialsEnum()
		{
			_taskManager.addTask(LoadMaterials());
		}

		private IEnumerator LoadMaterials()
		{
			for(int i = 0; i < _root.Materials.Count; ++i)
			{
				if (_useGLTFMaterial)
					CreateMaterial(_root.Materials[i], i);
				else
					CreateUnityMaterial(_root.Materials[i], i);

				setStatus("Loaded material " + (i + 1) + "/" + _root.Materials.Count, i != 0);
				yield return null;
			}
		}

		private Texture2D getTexture(int index)
		{
			return _assetSerializer.getTexture(index);
		}

		private UnityEngine.Material getMaterial(int index)
		{
			return _assetSerializer.getMaterial(index);
		}

		// Add support for vertex colors
		protected virtual void CreateUnityMaterial(GLTF.Schema.Material def, int materialIndex)
		{

			Extension specularGlossinessExtension = null;
			bool isSpecularPBR = def.Extensions !=null && def.Extensions.TryGetValue("KHR_materials_pbrSpecularGlossiness", out specularGlossinessExtension);

			Shader shader = isSpecularPBR ? Shader.Find("Standard (Specular setup)") : Shader.Find("Standard");

			var material = new UnityEngine.Material(shader);
			material.hideFlags = HideFlags.DontUnloadUnusedAsset;

			material.name = def.Name;
			if (def.AlphaMode == AlphaMode.MASK)
			{
				GLTFUtils.SetupMaterialWithBlendMode(material, GLTFUtils.BlendMode.Cutout);
				material.SetFloat("_Mode", 1);
				material.SetFloat("_Cutoff", (float)def.AlphaCutoff);
			}
			else if (def.AlphaMode == AlphaMode.BLEND)
			{
				GLTFUtils.SetupMaterialWithBlendMode(material, GLTFUtils.BlendMode.Fade);
				material.SetFloat("_Mode", 3);
			}

			if (def.NormalTexture != null)
			{
				var texture = def.NormalTexture.Index.Id;
				Texture2D normalTexture = getTexture(texture) as Texture2D;

				//Automatically set it to normal map
				TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normalTexture)) as TextureImporter;
				im.textureType = TextureImporterType.NormalMap;
				im.SaveAndReimport();
				material.SetTexture("_BumpMap", getTexture(texture));
				material.SetFloat("_BumpScale", (float)def.NormalTexture.Scale);
			}

			if (def.EmissiveTexture != null)
			{
				material.EnableKeyword("EMISSION_MAP_ON");
				var texture = def.EmissiveTexture.Index.Id;
				material.SetTexture("_EmissionMap", getTexture(texture));
				material.SetInt("_EmissionUV", def.EmissiveTexture.TexCoord);
			}

			material.SetColor("_EmissionColor", def.EmissiveFactor.ToUnityColor());

			if (specularGlossinessExtension != null)
			{
				KHR_materials_pbrSpecularGlossinessExtension pbr = (KHR_materials_pbrSpecularGlossinessExtension) specularGlossinessExtension;
				if (pbr.DiffuseTexture != null)
				{
					var texture = pbr.DiffuseTexture.Index.Id;
					material.SetTexture("_MainTex", getTexture(texture));
				}

				if(pbr.SpecularGlossinessTexture != null)
				{
					var texture = pbr.SpecularGlossinessTexture.Index.Id;
					material.SetTexture("_SpecGlossMap", getTexture(texture));
				}
				Vector3 specularVec3 = pbr.SpecularFactor.ToUnityVector3();
				material.SetColor("_SpecColor", new Color(specularVec3.x, specularVec3.y, specularVec3.z, 1.0f));
				material.SetFloat("_Glossiness", (float)pbr.GlossinessFactor);

				if (def.OcclusionTexture != null)
				{
					var texture = def.OcclusionTexture.Index.Id;
					material.SetFloat("_OcclusionStrength", (float)def.OcclusionTexture.Strength);
					material.SetTexture("_OcclusionMap", getTexture(texture));
				}

				GLTFUtils.SetMaterialKeywords(material, GLTFUtils.WorkflowMode.Specular);
			}
			else if (def.PbrMetallicRoughness != null)
			{
				var pbr = def.PbrMetallicRoughness;

				material.SetColor("_Color", pbr.BaseColorFactor.ToUnityColor());
				if (pbr.BaseColorTexture != null)
				{
					var texture = pbr.BaseColorTexture.Index.Id;
					material.SetTexture("_MainTex", getTexture(texture));
				}

				material.SetFloat("_Metallic", (float)pbr.MetallicFactor);
				material.SetFloat("_Glossiness", 1.0f - (float)pbr.RoughnessFactor);

				if (pbr.MetallicRoughnessTexture != null)
				{
					var texture = pbr.MetallicRoughnessTexture.Index.Id;
					UnityEngine.Texture2D inputTexture = getTexture(texture) as Texture2D;
					List<Texture2D> splitTextures = GLTFUtils.splitAndRemoveMetalRoughTexture(inputTexture, def.OcclusionTexture != null);
					material.SetTexture("_MetallicGlossMap", splitTextures[0]);

					if (def.OcclusionTexture != null)
					{
						material.SetFloat("_OcclusionStrength", (float)def.OcclusionTexture.Strength);
						material.SetTexture("_OcclusionMap", splitTextures[1]);
					}
				}

				GLTFUtils.SetMaterialKeywords(material, GLTFUtils.WorkflowMode.Metallic);
			}

			material = _assetSerializer.saveMaterial(material, materialIndex);
			_assetSerializer._parsedMaterials.Add(material);
		}

		protected virtual void CreateMaterial(GLTF.Schema.Material def, int materialIndex)
		{
			Shader shader;

			// get the shader to use for this material
			try
			{
				if (def.PbrMetallicRoughness != null)
					shader = _shaderCache[GLTFEditorImporter.MaterialType.PbrMetallicRoughness];
				else if (_root.ExtensionsUsed != null && _root.ExtensionsUsed.Contains("KHR_materials_common")
						 && def.CommonConstant != null)
					shader = _shaderCache[GLTFEditorImporter.MaterialType.CommonConstant];
				else
					shader = _shaderCache[GLTFEditorImporter.MaterialType.PbrMetallicRoughness];
			}
			catch (KeyNotFoundException)
			{
				Debug.LogWarningFormat("No shader supplied for type of glTF material {0}, using Standard fallback", def.Name);
				shader = Shader.Find("Standard");
			}

			//shader.maximumLOD = MaximumLod;

			var material = new UnityEngine.Material(Shader.Find("GLTF/GLTFStandard"));
			material = _assetSerializer.saveMaterial(material, materialIndex);

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

				material.SetColor("_Color", pbr.BaseColorFactor.ToUnityColor());

				if (pbr.BaseColorTexture != null)
				{
					var texture = pbr.BaseColorTexture.Index.Id;
					material.SetTexture("_MainTex", getTexture(texture));
				}

				material.SetFloat("_Metallic", (float) pbr.MetallicFactor);

				if (pbr.MetallicRoughnessTexture != null)
				{
					var texture = pbr.MetallicRoughnessTexture.Index.Id;
					material.SetTexture("_MetallicRoughnessMap", getTexture(texture));
				}

				material.SetFloat("_Roughness", (float) pbr.RoughnessFactor);
			}

			if (def.CommonConstant != null)
			{
				material.SetColor("_AmbientFactor", def.CommonConstant.AmbientFactor.ToUnityColor());

				if (def.CommonConstant.LightmapTexture != null)
				{
					material.EnableKeyword("LIGHTMAP_ON");

					var texture = def.CommonConstant.LightmapTexture.Index.Id;
					material.SetTexture("_LightMap", getTexture(texture));
					material.SetInt("_LightUV", def.CommonConstant.LightmapTexture.TexCoord);
				}

				material.SetColor("_LightFactor", def.CommonConstant.LightmapFactor.ToUnityColor());
			}

			if (def.NormalTexture != null)
			{
				var textureIndex = def.NormalTexture.Index.Id;
				_assetSerializer.setTextureNormalMap(textureIndex);
				Texture2D texture = getTexture(textureIndex);
				material.SetTexture("_BumpMap", texture);
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
					material.SetTexture("_OcclusionMap", getTexture(texture.Id));
				}
			}

			if (def.EmissiveTexture != null)
			{
				var texture = def.EmissiveTexture.Index.Id;
				material.EnableKeyword("EMISSION_MAP_ON");
				material.SetTexture("_EmissionMap", getTexture(texture));
				material.SetInt("_EmissionUV", def.EmissiveTexture.TexCoord);
			}

			material.SetColor("_EmissionColor", def.EmissiveFactor.ToUnityColor());
			_assetSerializer._parsedMaterials.Add(material);
		}


		private IEnumerator LoadMeshes()
		{
			for(int i = 0; i < _root.Meshes.Count; ++i)
			{
				CreateMeshObject(_root.Meshes[i], i);
				setStatus("Loaded mesh " + (i + 1) + "/" + _root.Meshes.Count, i != 0);
				yield return null;
			}
		}

		private IEnumerator LoadScene(int sceneIndex = -1)
		{
			Scene scene;
			_nbParsedNodes = 0;

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

			var sceneObj = new GameObject(_currentSampleName ?? "GLTFScene");
			foreach (var node in scene.Nodes)
			{
				var nodeObj = CreateNode(node.Value);
				nodeObj.transform.SetParent(sceneObj.transform, false);
			}

			_assetSerializer.savePrefab(sceneObj, _projectDirectoryPath);
			sceneObj.transform.position += _currentSampleIndex * _offset;
			yield return null;
		}


		protected virtual GameObject CreateNode(Node node)
		{
			var nodeObj = new GameObject(node.Name ?? "GLTFNode");
			//nodeObj.hideFlags = HideFlags.HideInHierarchy;

			_nbParsedNodes++;
			setStatus("Parsing node " +  _nbParsedNodes  + " / " +  _root.Nodes.Count, _nbParsedNodes != 1);

			Vector3 position;
			Quaternion rotation;
			Vector3 scale;
			node.GetUnityTRSProperties(out position, out rotation, out scale);
			nodeObj.transform.localPosition = position;
			nodeObj.transform.localRotation = rotation;
			nodeObj.transform.localScale = scale;

			if (node.Mesh != null)
			{
				// If several primitive, create several nodes and add them as child of this current Node
				MeshFilter meshFilter = nodeObj.AddComponent<MeshFilter>();
				meshFilter.sharedMesh = _assetSerializer.getMesh(node.Mesh.Id, 0);

				MeshRenderer meshRenderer = nodeObj.AddComponent<MeshRenderer>();
				meshRenderer.material = _assetSerializer.getMaterial(node.Mesh.Id, 0);

				for(int i = 1; i < _assetSerializer._parsedMeshData[node.Mesh.Id].Count; ++i)
				{
					GameObject go = new GameObject(node.Name ?? "GLTFNode_" + i );
					MeshFilter mf = go.AddComponent<MeshFilter>();
					mf.sharedMesh = _assetSerializer.getMesh(node.Mesh.Id, i);
					MeshRenderer mr = go.AddComponent<MeshRenderer>();
					mr.material = _assetSerializer.getMaterial(node.Mesh.Id, i);
					go.transform.SetParent(nodeObj.transform, false);
				}
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

		protected virtual void CreateMeshObject(GLTF.Schema.Mesh mesh, int meshId)
		{
			for(int i = 0; i < mesh.Primitives.Count; ++i)
			{
				var primitive = mesh.Primitives[i];
				CreateMeshPrimitive(primitive, mesh.Name, meshId, i); // Converted to mesh
			}
		}

		protected virtual void CreateMeshPrimitive(MeshPrimitive primitive, string meshName, int meshID, int primitiveIndex)
		{
			var meshAttributes = BuildMeshAttributes(primitive, meshID, primitiveIndex);
			var vertexCount = primitive.Attributes[SemanticProperties.POSITION].Value.Count;

			// todo optimize: There are multiple copies being performed to turn the buffer data into mesh data. Look into reducing them
			UnityEngine.Mesh mesh = new UnityEngine.Mesh
			{
				vertices = primitive.Attributes.ContainsKey(SemanticProperties.POSITION)
					? meshAttributes[SemanticProperties.POSITION].AccessorContent.AsVertices.ToUnityVector3()
					: null,
				normals = primitive.Attributes.ContainsKey(SemanticProperties.NORMAL)
					? meshAttributes[SemanticProperties.NORMAL].AccessorContent.AsNormals.ToUnityVector3()
					: null,

				uv = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(0))
					? meshAttributes[SemanticProperties.TexCoord(0)].AccessorContent.AsTexcoords.ToUnityVector2()
					: null,

				uv2 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(1))
					? meshAttributes[SemanticProperties.TexCoord(1)].AccessorContent.AsTexcoords.ToUnityVector2()
					: null,

				uv3 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(2))
					? meshAttributes[SemanticProperties.TexCoord(2)].AccessorContent.AsTexcoords.ToUnityVector2()
					: null,

				uv4 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(3))
					? meshAttributes[SemanticProperties.TexCoord(3)].AccessorContent.AsTexcoords.ToUnityVector2()
					: null,

				colors = primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
					? meshAttributes[SemanticProperties.Color(0)].AccessorContent.AsColors.ToUnityColor()
					: null,

				triangles = primitive.Indices != null
					? meshAttributes[SemanticProperties.INDICES].AccessorContent.AsTriangles
					: MeshPrimitive.GenerateTriangles(vertexCount),

				tangents = primitive.Attributes.ContainsKey(SemanticProperties.TANGENT)
					? meshAttributes[SemanticProperties.TANGENT].AccessorContent.AsTangents.ToUnityVector4()
					: null
			};

			mesh = _assetSerializer.saveMesh(mesh, meshName + "_" + meshID + "_" + primitiveIndex);
			UnityEngine.Material material = primitive.Material != null && primitive.Material.Id >= 0 ? getMaterial(primitive.Material.Id) : defaultMaterial;

			_assetSerializer.addPrimitiveMeshData(meshID, primitiveIndex, mesh, material);
		}

		protected virtual Dictionary<string, AttributeAccessor> BuildMeshAttributes(MeshPrimitive primitive, int meshID, int primitiveIndex)
		{
			Dictionary<string, AttributeAccessor> attributeAccessors = new Dictionary<string, AttributeAccessor>(primitive.Attributes.Count + 1);
			foreach (var attributePair in primitive.Attributes)
			{
				AttributeAccessor AttributeAccessor = new AttributeAccessor()
				{
					AccessorId = attributePair.Value,
					Buffer = _assetCache.BufferCache[attributePair.Value.Value.BufferView.Value.Buffer.Id]
				};

				attributeAccessors[attributePair.Key] = AttributeAccessor;
			}

			if (primitive.Indices != null)
			{
				AttributeAccessor indexBuilder = new AttributeAccessor()
				{
					AccessorId = primitive.Indices,
					Buffer = _assetCache.BufferCache[primitive.Indices.Value.BufferView.Value.Buffer.Id]
				};

				attributeAccessors[SemanticProperties.INDICES] = indexBuilder;
			}

			GLTFHelpers.BuildMeshAttributes(ref attributeAccessors);
			return attributeAccessors;
		}
	}
}

class TaskManager
{
	List<IEnumerator> _tasks;
	IEnumerator _current = null;

	public TaskManager()
	{
		_tasks = new List<IEnumerator>();
	}

	public void addTask(IEnumerator task)
	{
		_tasks.Add(task);
	}

	public bool play()
	{
		if(_tasks.Count > 0)
		{
			if (_current == null || !_current.MoveNext())
			{
				_current = _tasks[0];
				_tasks.RemoveAt(0);
			}
		}

		if (_current != null)
			return _current.MoveNext();
		else
			return false;
	}
}