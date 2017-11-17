using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class GLTFTextureUtils
{
	private static string _flipTexture = "GLTF/FlipTexture";
	private static string _packOcclusionMetalRough = "GLTF/PackOcclusionMetalRough";
	private static string _extractOcclusion = "GLTF/ExtractOcclusion";
	private static string _extractMetalSmooth =  "GLTF/ExtractMetalSmooth";
	private static string _convertBump = "GLTF/BumpToNormal";
	public static bool _useOriginalImages = true;

	public static void setUseOriginalImage(bool useOriginal)
	{
		_useOriginalImages = useOriginal;
	}

	public static void setSRGB(bool useSRGB)
	{
		GL.sRGBWrite = useSRGB;
	}

	// IMPORT
	public static void extractOcclusion(Texture2D occlusionMetalRoughMap, string outputPath)
	{
		Debug.Log("Extract occlusion");
		if (occlusionMetalRoughMap == null)
		{
			return;
		}
		GL.sRGBWrite = true;
		Material extractMaterial = new Material(Shader.Find(_extractOcclusion));
		extractMaterial.SetTexture("_OcclusionMetallicRoughMap", occlusionMetalRoughMap);

		Texture2D output = processTextureMaterial(occlusionMetalRoughMap, extractMaterial);
		writeTextureOnDisk(output, outputPath);
	}

	public static void writeTextureOnDisk(Texture2D texture, string outputPath)
	{
		File.WriteAllBytes(outputPath, texture.EncodeToPNG());
		AssetDatabase.Refresh();
	}

	// Export
	public static Texture2D bumpToNormal(Texture2D texture)
	{
		Material convertBump = new Material(Shader.Find(_convertBump));
		convertBump.SetTexture("_BumpMap", texture);
		return processTextureMaterial(texture, convertBump);
	}

	public static bool isNormalMapFromGrayScale(ref Texture2D texture)
	{
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
		if (im == null)
			return false;

		return im.convertToNormalmap;
	}

	public static void packOcclusionMetalRoughMaterial(ref Material material, string outputPath = "")
	{
		Texture2D metalSmooth = material.GetTexture("_MetallicGlossMap") as Texture2D;
		Texture2D occlusion = material.GetTexture("_OcclusionMap") as Texture2D;

		writeTextureOnDisk(packOcclusionMetalRough(metalSmooth, occlusion), outputPath);
	}

	public static Texture2D packOcclusionMetalRough(Texture2D metallicSmoothnessMap, Texture2D occlusionMap)
	{
		if(metallicSmoothnessMap == null && occlusionMap == null)
		{
			return null;
		}

		bool srgb = GL.sRGBWrite;
		GL.sRGBWrite = false;

		Material packMaterial = new Material(Shader.Find(_packOcclusionMetalRough));
		Texture2D tex = null;
		int w = -1;
		int h = -1;
		if(metallicSmoothnessMap)
		{
			tex = metallicSmoothnessMap;
			packMaterial.SetTexture("_MetallicGlossMap", metallicSmoothnessMap);
		}

		if(occlusionMap)
		{
			if(tex == null)
			{
				tex = occlusionMap;
			}

			packMaterial.SetTexture("_OcclusionMap", occlusionMap);
		}
		Texture2D result = processTextureMaterial(tex, packMaterial);
		GL.sRGBWrite = srgb;
		return result;
	}
	
	// CORE
	private static Texture2D processTextureMaterial(Texture2D texture, Material blitMaterial)
	{
		var exportTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
		exportTexture.name = texture.name;

		var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 32, RenderTextureFormat.ARGB32);
		Graphics.Blit(exportTexture, renderTexture, blitMaterial);
		RenderTexture.active = renderTexture;

		exportTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
		exportTexture.Apply();

		return exportTexture;
	}

	// Normal map should be exported with srgb true
	public static Texture2D handleNormalMap(Texture2D input)
	{
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(input)) as TextureImporter;
		if(AssetDatabase.GetAssetPath(input).Length == 0 || im == null || im.convertToNormalmap)
		{
			Debug.Log("Convert bump to normal " + input.name);
			return bumpToNormal(input);
		}
		else
		{
			return getTexture(input);
		}
	}

	private static Texture2D getTexture(Texture2D texture)
	{
		Texture2D temp = new Texture2D(4, 4);
		temp.name = texture.name;
		if (_useOriginalImages)
		{
			Debug.Log(AssetDatabase.GetAssetPath(texture));
			if (AssetDatabase.GetAssetPath(texture).Length > 0)
			{
				temp.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)));
				temp.name = texture.name;
			}
			else
			{
				temp = texture;
				Debug.Log("Texture asset is not serialized. Cannot use uncompressed version for " + texture.name);
			}
		}
		else
		{
			temp = texture;
		}

		return temp;
	}

	public static Texture2D flipTexture(Texture2D texture)
	{
		Material flipMaterial = new Material(Shader.Find(_flipTexture));
		Texture2D temp = texture;

		flipMaterial.SetTexture("_TextureToFlip", temp);
		return processTextureMaterial(temp, flipMaterial);
	}
}

public class GLTFTextureWrapper
{
	public enum OPERATION
	{
		NONE,
		FLIPY,
		OCCLUSION_METALIC_ROUGHNESS, 
		BUMP_TO_NORMAL
	}

	List<Texture2D> _inputs;
	OPERATION _operation;

	GLTFTextureWrapper()
	{
		_inputs = new List<Texture2D>();
	}

	public GLTFTextureWrapper(Texture2D texture, OPERATION operation)
	{
		_inputs.Add(texture);
		_operation = operation;
	}

	public void AddTexture(Texture2D texture)
	{
		_inputs.Add(texture);
	}
}

public class GLTFTextureManager
{
	List<GLTFTextureWrapper> _imagesProcess;
	GLTFTextureManager()
	{
		_imagesProcess = new List<GLTFTextureWrapper>();
	}

	public int registerSimpleTexture(Texture2D texture, bool flipY = true)
	{
		int index = _imagesProcess.Count;
		_imagesProcess.Add(new GLTFTextureWrapper(texture, (flipY ? GLTFTextureWrapper.OPERATION.FLIPY : GLTFTextureWrapper.OPERATION.NONE)));

		return index;
	}

	public int registerMetalOcclusionTexture(Texture2D metalSmooth, Texture2D occlusion)
	{
		int index = _imagesProcess.Count;
		GLTFTextureWrapper tw = new GLTFTextureWrapper(metalSmooth, GLTFTextureWrapper.OPERATION.OCCLUSION_METALIC_ROUGHNESS);
		tw.AddTexture(occlusion);

		_imagesProcess.Add(tw);
		return index;
	}

	public int registerNormalMap(Texture2D normalMap)
	{
		int index = _imagesProcess.Count;
		_imagesProcess.Add(new GLTFTextureWrapper(normalMap, GLTFTextureWrapper.OPERATION.BUMP_TO_NORMAL));

		return index;
	}
}
