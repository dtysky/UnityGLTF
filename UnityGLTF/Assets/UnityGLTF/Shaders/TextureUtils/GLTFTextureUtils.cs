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

	public static void extractMetalRough(Texture2D occlusionMetalRoughMap, string outputPath)
	{
		Debug.Log("Extract Metla rough");
		if (occlusionMetalRoughMap == null)
		{
			return;
		}

		GL.sRGBWrite = true;
		Material extractMaterial = new Material(Shader.Find(_extractMetalSmooth));
		extractMaterial.SetTexture("_OcclusionMetallicRoughMap", occlusionMetalRoughMap);

		Texture2D output = processTextureMaterial(occlusionMetalRoughMap, extractMaterial);
		writeTextureOnDisk(output, outputPath);
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
		var exportTexture = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, false);
		exportTexture.name = texture.name;

		var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 32, RenderTextureFormat.ARGB32);
		Graphics.Blit(exportTexture, renderTexture, blitMaterial);
		RenderTexture.active = renderTexture;

		exportTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
		exportTexture.Apply();

		RenderTexture.ReleaseTemporary(renderTexture);

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
