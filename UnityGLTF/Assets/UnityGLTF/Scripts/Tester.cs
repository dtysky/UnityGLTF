#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;

public class Tester : EditorWindow
{
	public static string directory = "Assets/Test/exportTextures/";
	public static List<string> channels;
	public static  MODE mode;

	// 
	public static Texture2D MetalSmooth;
	public static Texture2D Occlusion;
	public static Texture2D GLTFOccMetalRough;
	public static Texture2D FlipTex;
	public static Texture2D BumpTex;
	public static Texture2D NormalTex;

	public enum MODE
	{
		METAL_ROUGH = 1,
		NORMAL = 2
	}

	[MenuItem("Tools/Test %t")]
	public static void Init()
	{
		EditorWindow.GetWindow(typeof(Tester));
		mode = MODE.METAL_ROUGH;
	}

	void setupChannels()
	{
		channels = new List<string>();
		channels.Add("_MetallicGlossMap");
		channels.Add("_OcclusionMap");
		channels.Add("_BumpMap");
	}
	public void OnGUI()
	{
		if(channels == null)
		{
			setupChannels();
		}

		MetalSmooth = EditorGUILayout.ObjectField(MetalSmooth, typeof(Texture2D)) as Texture2D;
		Occlusion = EditorGUILayout.ObjectField(Occlusion, typeof(Texture2D)) as Texture2D;
		if (GUILayout.Button("Pack occlusion metal rough"))
		{
			packOccMetalRough();
		}

		GUILayout.Label("");
		GUILayout.Label("");
		GUILayout.Label("");

		GLTFOccMetalRough = EditorGUILayout.ObjectField(GLTFOccMetalRough, typeof(Texture2D)) as Texture2D;
		if (GUILayout.Button("Extract occlusion"))
		{
			extractOcclusion();
		}


		GUILayout.Label("");
		GUILayout.Label("");
		GUILayout.Label("");

		FlipTex = EditorGUILayout.ObjectField(FlipTex, typeof(Texture2D)) as Texture2D;
		if (GUILayout.Button("FLip Texture"))
		{
			flipTexture();
		}

		GUILayout.Label("");
		GUILayout.Label("");
		GUILayout.Label("");

		BumpTex = EditorGUILayout.ObjectField(BumpTex, typeof(Texture2D)) as Texture2D;
		if (GUILayout.Button("Convert bump to Normal"))
		{
			convertBump();
		}

		GUILayout.Label("");
		GUILayout.Label("");
		GUILayout.Label("");

		NormalTex = EditorGUILayout.ObjectField(NormalTex, typeof(Texture2D)) as Texture2D;
		if (GUILayout.Button("Convert bump to Normal"))
		{
			handleNormalMap();
		}
	}

	public void packOccMetalRough()
	{
		GLTFTextureUtils.writeTextureOnDisk(GLTFTextureUtils.packOcclusionMetalRough(MetalSmooth, Occlusion), Path.Combine(directory, MetalSmooth.name + ".png"));
	}

	public void convertBump()
	{
		bool srgb = GL.sRGBWrite;
		GL.sRGBWrite = false;
		GLTFTextureUtils.writeTextureOnDisk(GLTFTextureUtils.handleNormalMap(BumpTex), Path.Combine(directory, "bump.png"));
		GL.sRGBWrite = srgb;
	}
	public void handleNormalMap()
	{
		bool srgb = GL.sRGBWrite;
		GL.sRGBWrite = true;
		GLTFTextureUtils.writeTextureOnDisk(GLTFTextureUtils.handleNormalMap(NormalTex), Path.Combine(directory, "originalNormal.png"));
		GL.sRGBWrite = srgb;
	}
	public void extractOcclusion()
	{
		GLTFTextureUtils.extractOcclusion(GLTFOccMetalRough, Path.Combine(directory, GLTFOccMetalRough.name + ".png"));
	}

	public void flipTexture()
	{
		GLTFTextureUtils.writeTextureOnDisk(GLTFTextureUtils.flipTexture(FlipTex), Path.Combine(directory, "flipped.png"));
	}
}
#endif