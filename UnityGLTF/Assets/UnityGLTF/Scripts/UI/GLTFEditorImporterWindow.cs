using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityGLTF;
using Ionic.Zip;

class GLTFEditorImporterWindow : EditorWindow
{
	[MenuItem("Tools/Import glTF %_e")]
	static void Init()
	{
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX // edit: added Platform Dependent Compilation - win or osx standalone
		GLTFEditorImporterWindow window = (GLTFEditorImporterWindow)EditorWindow.GetWindow(typeof(GLTFEditorImporterWindow));
		window.titleContent.text = "glTF importer";
		window.Show(true);
#else // and error dialog if not standalone
		EditorUtility.DisplayDialog("Error", "Your build target must be set to standalone", "Okay");
#endif
	}

	// Public
	public bool _useGLTFMaterial = false;
	private bool _userStopped = false;
	public bool ENABLE_DEBUG = true;

	//Tests
	private string _defaultImportDirectory = "Import";
	private static string _currentSampleName = "Imported";
	private List<string> _unzippedFiles;

	GLTFEditorImporter _importer;

	string _gltfPath = "";
	string _projectDirectory = "";
	string _unzipDirectory = "";
	bool _isInitialized = false;
	GUIStyle _header;

	private void Initialize()
	{
		_importer = new GLTFEditorImporter(this.Repaint);
		_unzippedFiles = new List<string>();
		_isInitialized = true;
		_unzipDirectory = Application.temporaryCachePath + "/unzip";
		_header = new GUIStyle(EditorStyles.boldLabel);
	}

	private string unzipGltfArchive(string zipPath)
	{
		// Clean previously unzipped files
		GLTFUtils.removeFileList(_unzippedFiles.ToArray());
		GLTFUtils.removeEmptyDirectory(_unzipDirectory);
		
		// Extract archive
		ZipFile zipfile = ZipFile.Read(zipPath);
		zipfile.ExtractAll(_unzipDirectory, ExtractExistingFileAction.OverwriteSilently);

		string gltfFile = "";
		DirectoryInfo info = new DirectoryInfo(_unzipDirectory);
		foreach (FileInfo fileInfo in info.GetFiles())
		{
			_unzippedFiles.Add(fileInfo.FullName);
			if (Path.GetExtension(fileInfo.FullName) == ".gltf")
			{
				gltfFile = fileInfo.FullName;
			}
		}

		return gltfFile;
	}

	private void checkValidity()
	{
		if(_importer == null)
		{
			Initialize();
		}
	}

	public void OnDestroy()
	{
		GLTFUtils.removeFileList(_unzippedFiles.ToArray());
		GLTFUtils.removeEmptyDirectory(_unzipDirectory);
	}

	public void Update()
	{
		if(_importer != null)
		{
			_importer.Update();
			Repaint();
		}
	}

	private void OnGUI()
	{
		if (!_isInitialized)
			Initialize();

		checkValidity();

		DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
		if (Event.current.type == EventType.DragExited)
		{
			_gltfPath = DragAndDrop.paths[0];
		}

		// Import file
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		GUILayout.Label("Import or Drag'n drop glTF asset(gltf, glb, zip supported)", _header);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (GUILayout.Button("Import file from disk"))
		{
			_gltfPath = EditorUtility.OpenFilePanel("Choose glTF to import", Application.dataPath, "*gl*;*zip");
			string modeldir = Path.GetFileNameWithoutExtension(_gltfPath);
			_projectDirectory = Path.Combine(_defaultImportDirectory, modeldir);
		}

		GUILayout.Label("Paths");
		GUILayout.BeginVertical("Box");
		GUILayout.Label("Model to import: " + _gltfPath);
		GUILayout.Label("Import directory: " + _projectDirectory);
		GUILayout.EndVertical();

		// Options
		GUILayout.Label("");
		GUILayout.Label("Options", _header);
		GUILayout.BeginHorizontal();
		GUILayout.Label("Prefab name:");
		_currentSampleName = GUILayout.TextField(_currentSampleName, GUILayout.Width(250));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		_useGLTFMaterial = GUILayout.Toggle(_useGLTFMaterial, "Use GLTF specific shader");
		GUI.enabled = _gltfPath.Length > 0 && File.Exists(_gltfPath);

		GUILayout.Label("");
		GUILayout.Label("");

		// Import button
		if (GUILayout.Button("IMPORT"))
		{
			_userStopped = false;
			_projectDirectory = EditorUtility.OpenFolderPanel("Choose import directory in Project", Application.dataPath, "Assets");
			Directory.CreateDirectory(_projectDirectory);
			if(Path.GetExtension(_gltfPath) == ".zip")
			{
				_gltfPath = unzipGltfArchive(_gltfPath);
				Debug.Log(_gltfPath);
			}
			_importer.setupForPath(_gltfPath, _projectDirectory, _currentSampleName);
			_importer.Load();
		}

		GUI.enabled = true;
		GUILayout.BeginHorizontal("Box");
		GUILayout.Label("Status: " + _importer.getStatus());
		GUILayout.EndHorizontal();
	}
}
