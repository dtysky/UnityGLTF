using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityGLTF.Cache;

public class AssetManager
{
	protected string _importDirectory;
	protected string _importMeshesDirectory;
	protected string _importMaterialsDirectory;
	protected string _importTexturesDirectory;
	protected string _prefabName;

	Dictionary<int, UnityEngine.Object> _registeredAsset;
	List<Object> _assetsToSerialize;

	// Store generated data
	public List<List<KeyValuePair<Mesh, Material>>> _parsedMeshData;
	public List<Material> _parsedMaterials;
	public List<string> _parsedImages;
	public List<Texture2D> _parsedTextures;
	public AssetManager(string projectDirectoryPath, string glTFUrl)
	{
		// Prepare hierarchy un project
		_importDirectory = projectDirectoryPath;
		_registeredAsset = new Dictionary<int, Object>();
		_assetsToSerialize = new List<Object>();

		_importTexturesDirectory = _importDirectory + "/textures";
		Directory.CreateDirectory(_importTexturesDirectory);

		_importMeshesDirectory = _importDirectory + "/meshes";
		Directory.CreateDirectory(_importMeshesDirectory);

		_importMaterialsDirectory = _importDirectory + "/materials";
		Directory.CreateDirectory(_importMaterialsDirectory);

		_prefabName = Path.GetFileNameWithoutExtension(glTFUrl);

		_parsedMeshData = new List<List<KeyValuePair<Mesh, Material>>>();
		_parsedMaterials = new List<Material>();
		_parsedImages = new List<string>();
		_parsedTextures = new List<Texture2D>();
	}

	public void addAssetToSerialize(Object asset)
	{
		_assetsToSerialize.Add(asset);
	}

	public void addPrimitiveMeshData(int meshIndex, int primitiveIndex, UnityEngine.Mesh mesh, UnityEngine.Material material)
	{
		if(meshIndex >= _parsedMeshData.Count)
		{
			_parsedMeshData.Add(new List<KeyValuePair<Mesh, Material>>());
		}

		if(primitiveIndex != _parsedMeshData[meshIndex].Count)
		{
			Debug.LogError("Array offset in mesh data");
		}

		_parsedMeshData[meshIndex].Add(new KeyValuePair<Mesh, Material>(mesh, material));
	}

	public Mesh getMesh(int nodeIndex, int primitiveIndex)
	{
		return _parsedMeshData[nodeIndex][primitiveIndex].Key;
	}

	public Material getMaterial(int nodeIndex, int primitiveIndex)
	{
		return _parsedMeshData[nodeIndex][primitiveIndex].Value;
	}

	public UnityEngine.Material getMaterial(int index)
	{
		return _parsedMaterials[index];
	}

	public UnityEngine.Texture2D getTexture(int index)
	{
		return _parsedTextures[index];
	}

	public void setTextureNormalMap(int index)
	{
		Texture2D texture = _parsedTextures[index];
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
		im.textureType = TextureImporterType.NormalMap;
		im.SaveAndReimport();
	}

	public void updateTexture(Texture2D texture, int imageIndex, int textureIndex)
	{
		string assetPath = GLTFUtils.getPathProjectFromAbsolute(_parsedImages[imageIndex]);
		AssetDatabase.Refresh();
		Texture2D newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		_parsedTextures[textureIndex] = newTex;
	}

	public Mesh saveMesh(Mesh mesh, string objectName = "Scene")
	{
		if(!_registeredAsset.ContainsKey(mesh.GetInstanceID()))
		{
			string meshProjectPath = GLTFUtils.getPathProjectFromAbsolute(_importMeshesDirectory) + "/" + objectName + "_" + _registeredAsset.Count + ".asset";
			meshProjectPath = meshProjectPath.Replace(":", "_");
			AssetDatabase.CreateAsset(mesh, meshProjectPath);
			AssetDatabase.Refresh();
			_registeredAsset.Add(mesh.GetInstanceID(), AssetDatabase.LoadAssetAtPath(meshProjectPath, typeof(Mesh)));
		}
		return (Mesh) _registeredAsset[mesh.GetInstanceID()];
	}

	private Texture2D saveTexture(Texture2D texture, int index)
	{

		if(!_registeredAsset.ContainsKey(texture.GetInstanceID()))
		{
			string textureAbsolutePath = _importTexturesDirectory + "/" + texture.name + "_" + index;
			string textureProjectPath = GLTFUtils.getPathProjectFromAbsolute(_importTexturesDirectory) + "/" +texture.name + "_" + index;
			string texturepath = textureProjectPath;
			if (texture.format == TextureFormat.ARGB32)
			{
				byte[] textureData = texture.EncodeToPNG();
				File.WriteAllBytes(textureAbsolutePath + ".png", textureData);
				AssetDatabase.Refresh();
				textureProjectPath = textureProjectPath + ".png";
			}
			else
			{
				byte[] textureData = texture.EncodeToJPG();
				File.WriteAllBytes(textureAbsolutePath +".jpg", textureData);
				AssetDatabase.Refresh();
				textureProjectPath  = textureProjectPath + ".jpg";
			}

			Texture2D unityTexture2D = (Texture2D)AssetDatabase.LoadAssetAtPath(textureProjectPath, typeof(Texture2D));
			_registeredAsset.Add(texture.GetInstanceID(), unityTexture2D);
		}

		return (Texture2D) _registeredAsset[texture.GetInstanceID()];

	}

	public Material saveMaterial(Material material, int index)
	{
		if (!_registeredAsset.ContainsKey(material.GetInstanceID()))
		{
			string name = material.name.Length > 0 ? material.name.Replace("/", "_").Replace(":","_") : "material";
			string materialProjectPath = GLTFUtils.getPathProjectFromAbsolute(_importMaterialsDirectory) + "/" + name + "_" + index + ".mat";

			if (!File.Exists(GLTFUtils.getPathAbsoluteFromProject(materialProjectPath)))
			{
				AssetDatabase.CreateAsset(material, materialProjectPath);
				AssetDatabase.Refresh();
			}

			Material unityMaterial = (Material)AssetDatabase.LoadAssetAtPath(materialProjectPath, typeof(Material));
			_registeredAsset.Add(material.GetInstanceID(), unityMaterial);
		}

		return (Material) _registeredAsset[material.GetInstanceID()];
	}

	private void collectWholeTree(Transform transform, ref List<Transform> collected)
	{
		foreach(Transform tr in transform)
		{
			collected.Add(tr);
			collectWholeTree(tr, ref collected);
		}
	}

	public void savePrefab(GameObject sceneObject, string _importDirectory)
	{
		string prefabPathInProject = GLTFUtils.getPathProjectFromAbsolute(_importDirectory);
		Object prefab = PrefabUtility.CreateEmptyPrefab(prefabPathInProject + "/" + _prefabName + ".prefab");
		PrefabUtility.ReplacePrefab(sceneObject, prefab, ReplacePrefabOptions.ConnectToPrefab);
	}

	public string copyTextureInProject(string imagePath)
	{
		string destPath = Path.Combine(_importTexturesDirectory, Path.GetFileName(imagePath));
		File.Copy(imagePath, destPath, true);
		AssetDatabase.Refresh();
		return destPath;
	}
}

