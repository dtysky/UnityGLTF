
using UnityEngine;
using UnityEditor;
using GLTF;
using System.IO;
using Ionic.Zip;
using System.Collections.Generic;

public class GLTFImporterWindow : EditorWindow
{
	string path = "";
	string importDirectory = "D:/Repositories/UnityGLTF/Assets/Samples";
	GameObject parentGameObject = null;
	public static string currentStatus;
	private GLTFFileLoader loader = null;
	// DUMMY
	UnityEngine.Material mat = null;
	static Texture2D dropImg;
	[MenuItem("Tools/Import glTF #_z")]
	static void CreateWindow()
	{
		GLTFImporterWindow window = (GLTFImporterWindow)EditorWindow.GetWindow(typeof(GLTFImporterWindow));
		window.title = "Import glTF";
		window.Show();
		dropImg = (Texture2D)Resources.Load(Application.dataPath + "/GLTF/Resources/drop.png", typeof(Texture2D));
	}

	public string getPathAbsoluteFromProject(string projectPath)
	{
		return projectPath.Replace("Assets/", Application.dataPath);
	}

	private void OnGUI()
	{
		if(!dropImg)
			dropImg = (Texture2D)AssetDatabase.LoadAssetAtPath("Assets/GLTF/Resources/drop.png", typeof(Texture2D));

		DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
		if (Event.current.type == EventType.DragExited)
		{
			string zipPath = DragAndDrop.paths[0];
			ZipFile zipfile = ZipFile.Read(zipPath);

			currentStatus = "Importing dropped file: " + zipPath;
			string zipDir = Path.GetDirectoryName(zipPath);
			string unzipDir = Application.temporaryCachePath + "/" + Path.GetFileNameWithoutExtension(zipPath);
			// clean directory
			if (Directory.Exists(unzipDir))
			{
				Directory.Delete(unzipDir, true);
			}

			Directory.CreateDirectory(unzipDir);

			zipfile.ExtractAll(unzipDir);
			string gltfFile = "";
			var info = new DirectoryInfo(unzipDir);
			foreach(FileInfo fileInfo in info.GetFiles())
			{
				if (Path.GetExtension(fileInfo.FullName) == ".gltf")
				{
					gltfFile = fileInfo.FullName;
					currentStatus = "Found glTF file : " + gltfFile;
				}
					
			}

			importDirectory = EditorUtility.OpenFolderPanel("Choose import directory in Project", Application.dataPath, "Samples");
			Debug.Log(importDirectory);
			currentStatus = "Preparing to import assets in directory: " + importDirectory;
			if (!Directory.Exists(importDirectory))
			{
				Debug.Log("Directory" + importDirectory + " doesn't exist");
			}	
			importDirectory = GUILayout.TextField(importDirectory);
			if(gltfFile.Length > 0 && File.Exists(gltfFile))
			{
				currentStatus = "Staring import (may freeze UI a few seconds)";
				ImportFile(gltfFile);
			}
			

			// clean
			Directory.Delete(unzipDir, true);
		}

		GUILayout.BeginHorizontal("Box");
		GUILayout.FlexibleSpace();
		GUILayout.Label("Drop a glTF .zip file to import it");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		GUILayout.Label(dropImg);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private void ImportFile(string path)
	{
		loader = new GLTFFileLoader(path, null, true, importDirectory);
		loader.Load();
	}

	private void OnDestroy()
	{
		DestroyImmediate(parentGameObject);
		EditorApplication.isPlaying = false;
	}
}