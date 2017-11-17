using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class GLTFUtils
{
	public enum WorkflowMode
	{
		Specular,
		Metallic,
		Dielectric
	}

	public enum BlendMode
	{
		Opaque,
		Cutout,
		Fade,   // Old school alpha-blending mode, fresnel does not affect amount of transparency
		Transparent // Physically plausible transparency mode, implemented as alpha pre-multiply
	}

	public enum SmoothnessMapChannel
	{
		SpecularMetallicAlpha,
		AlbedoAlpha,
	}

	public static string getPathProjectFromAbsolute(string absolutePath)
	{
		return absolutePath.Replace(Application.dataPath, "Assets").Replace(":", "_");
	}

	public static string getPathAbsoluteFromProject(string projectPath)
	{
		return projectPath.Replace("Assets/", Application.dataPath).Replace(":", "_");
	}

	public static List<UnityEngine.Texture2D> splitAndRemoveMetalRoughTexture(Texture2D inputTexture, bool hasOcclusion)
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
		string occlusionPath = "";
		if (hasOcclusion)
		{
			Texture2D occlusionTexture = new Texture2D(width, height);
			occlusionTexture.SetPixels(occlusion);
			occlusionTexture.Apply();

			occlusionPath = Path.GetDirectoryName(inputTexturePath) + "/" + Path.GetFileNameWithoutExtension(inputTexturePath) + "_occlusion" + Path.GetExtension(inputTexturePath);
			File.WriteAllBytes(occlusionPath, occlusionTexture.EncodeToPNG());
		}

		// Delete original texture
		AssetDatabase.DeleteAsset(inputTexturePath);
		AssetDatabase.Refresh();

		Texture2D metalTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(metalRoughPath, typeof(Texture2D));
		outputs.Add(metalTexture);
		if (hasOcclusion)
		{
			Texture2D occlusionTextureOutput = (Texture2D)AssetDatabase.LoadAssetAtPath(occlusionPath, typeof(Texture2D));
			outputs.Add(occlusionTextureOutput);
		}

		return outputs;
	}

	private static bool getPixelsFromTexture(ref Texture2D texture, out Color[] pixels)
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
	public static void SetupMaterialWithBlendMode(Material material, BlendMode blendMode)
	{
		switch (blendMode)
		{
			case BlendMode.Opaque:
				material.SetOverrideTag("RenderType", "");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				material.SetInt("_ZWrite", 1);
				material.DisableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = -1;
				break;
			case BlendMode.Cutout:
				material.SetOverrideTag("RenderType", "TransparentCutout");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				material.SetInt("_ZWrite", 1);
				material.EnableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				break;
			case BlendMode.Fade:
				material.SetOverrideTag("RenderType", "Transparent");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				material.SetInt("_ZWrite", 0);
				material.DisableKeyword("_ALPHATEST_ON");
				material.EnableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				break;
			case BlendMode.Transparent:
				material.SetOverrideTag("RenderType", "Transparent");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				material.SetInt("_ZWrite", 0);
				material.DisableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				break;
		}
	}

	public static SmoothnessMapChannel GetSmoothnessMapChannel(Material material)
	{
		int ch = (int)material.GetFloat("_SmoothnessTextureChannel");
		if (ch == (int)SmoothnessMapChannel.AlbedoAlpha)
			return SmoothnessMapChannel.AlbedoAlpha;
		else
			return SmoothnessMapChannel.SpecularMetallicAlpha;
	}

		public static void SetMaterialKeywords(Material material, WorkflowMode workflowMode)
		{
			// Note: keywords must be based on Material value not on MaterialProperty due to multi-edit & material animation
			// (MaterialProperty value might come from renderer material property block)
			SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") || material.GetTexture("_DetailNormalMap"));
			if (workflowMode == WorkflowMode.Specular)
				SetKeyword(material, "_SPECGLOSSMAP", material.GetTexture("_SpecGlossMap"));
			else if (workflowMode == WorkflowMode.Metallic)
				SetKeyword(material, "_METALLICGLOSSMAP", material.GetTexture("_MetallicGlossMap"));
			SetKeyword(material, "_PARALLAXMAP", material.GetTexture("_ParallaxMap"));
			SetKeyword(material, "_DETAIL_MULX2", material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap"));

			// A material's GI flag internally keeps track of whether emission is enabled at all, it's enabled but has no effect
			// or is enabled and may be modified at runtime. This state depends on the values of the current flag and emissive color.
			// The fixup routine makes sure that the material is in the correct state if/when changes are made to the mode or color.
			MaterialEditor.FixupEmissiveFlag(material);
			//bool shouldEmissionBeEnabled = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
			SetKeyword(material, "_EMISSION", material.GetTexture("_EmissionMap"));

			if (material.HasProperty("_SmoothnessTextureChannel"))
			{
				SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", GetSmoothnessMapChannel(material) == SmoothnessMapChannel.AlbedoAlpha);
			}
		}

		public static void MaterialChanged(Material material, WorkflowMode workflowMode)
		{
			SetupMaterialWithBlendMode(material, (BlendMode)material.GetFloat("_Mode"));

			SetMaterialKeywords(material, workflowMode);
		}

		public static void SetKeyword(Material m, string keyword, bool state)
		{
			if (state)
				m.EnableKeyword(keyword);
			else
				m.DisableKeyword(keyword);
		}
	}
