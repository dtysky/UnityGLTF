/*
 * Copyright(c) 2017-2018 Sketchfab Inc.
 * License: https://github.com/sketchfab/UnityGLTF/blob/master/LICENSE
 */
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityGLTF;
using Ionic.Zip;

namespace Sketchfab
{
    class SketchfabImporterWindow : EditorWindow
    {
        [MenuItem("Sketchfab/Import glTF")]
        static void Init()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            SketchfabImporterWindow window = (SketchfabImporterWindow)EditorWindow.GetWindow(typeof(SketchfabImporterWindow));
            window.titleContent.text = "Importer";
            window.Show(true);
#else // and error dialog if not standalone
		EditorUtility.DisplayDialog("Error", "Your build target must be set to standalone", "Okay");
#endif
        }

        // Public
        public bool _useGLTFMaterial = false;

        private string _defaultImportDirectory = "";
        private static string _currentSampleName = "Imported";
        GLTFEditorImporter _importer;
        string _importFilePath = "";
        string _gltfPath ="";
        string _importDirectory = "";
        string _unzipDirectory = "";
        private List<string> _unzippedFiles;
        bool _isInitialized = false;
        GUIStyle _header;
        Sketchfab.SketchfabAPI _api;
        Vector2 minimumSize = new Vector2(603, 450);
        bool _addToCurrentScene = false;
        string _prefabName = "Imported";
        Vector2 UI_SIZE = new Vector2(350, 21);
        SketchfabUI _ui;
        Vector2 _scrollView;

        string _sourceFileHint = "Select or drag and drop a file";

    private void Initialize()
        {
            SketchfabPlugin.Initialize();
            _importer = new GLTFEditorImporter(UpdateProgress, OnFinishImport);
            _unzippedFiles = new List<string>();
            _isInitialized = true;
            _unzipDirectory = Application.temporaryCachePath + "/unzip";
            _header = new GUIStyle(EditorStyles.boldLabel);
            _defaultImportDirectory = Application.dataPath + "/Import";
            _importDirectory = _defaultImportDirectory;
            _importFilePath = _sourceFileHint;
            _ui = SketchfabPlugin.getUI();
        }

        void OnCheckVersionFailure()
        {
            Debug.Log("Failed to retrieve Plugin version");
        }

        private string findGltfFile()
        {
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

        private string unzipGltfArchive(string zipPath)
        {
            if (!Directory.Exists(_unzipDirectory))
                Directory.CreateDirectory(_unzipDirectory);

            // Clean previously unzipped files
            GLTFUtils.removeFileList(_unzippedFiles.ToArray());
            string gltfFile = findGltfFile();
            if (gltfFile != "")
            {
                File.Delete(gltfFile);
            }

            // Extract archive
            ZipFile zipfile = ZipFile.Read(zipPath);
            zipfile.ExtractAll(_unzipDirectory, ExtractExistingFileAction.OverwriteSilently);

            return findGltfFile();
        }

        private string unzipGltfArchive(byte[] zipData)
        {


            return findGltfFile();
        }

        private void checkValidity()
        {
            SketchfabPlugin.checkValidity();
            if(_ui == null)
            {
                _ui = new SketchfabUI();
            }
            if (_importer == null)
            {
                Initialize();
            }
        }

        public void OnDestroy()
        {
            GLTFUtils.removeFileList(_unzippedFiles.ToArray());
            GLTFUtils.removeEmptyDirectory(_unzipDirectory);
        }

        private void OnGUI()
        {
            float minWidthButton = 150;
            checkValidity();
            SketchfabPlugin.displayHeader();
            _scrollView = GUILayout.BeginScrollView(_scrollView);
            DragAndDrop.visualMode = DragAndDropVisualMode.Generic;

            if (Event.current.type == EventType.DragExited)
            {
                if (DragAndDrop.paths.Length > 0)
                {
                    _importFilePath = DragAndDrop.paths[0];
                    string modelfileName = Path.GetFileNameWithoutExtension(_importFilePath);
                    _importDirectory = GLTFUtils.unifyPathSeparator(Path.Combine(_defaultImportDirectory, modelfileName));
                    _currentSampleName = modelfileName;
                }
            }

            if (_ui == null)
                return;

            GUILayout.Label("Import a glTF (*.gltf, *.glb, *.zip)", _ui.sketchfabModelName);

            _ui.displaySubContent("Source file:");
            GUILayout.BeginHorizontal();
            Color backup = GUI.color;
            if (_importFilePath == _sourceFileHint)
                GUI.contentColor = Color.red;

            GUILayout.TextField(_importFilePath, GUILayout.MinWidth(UI_SIZE.x), GUILayout.Height(UI_SIZE.y));
            GUI.contentColor = backup;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select file", GUILayout.Height(UI_SIZE.y), GUILayout.Width(minWidthButton)))
            {
                string newImportDir = EditorUtility.OpenFolderPanel("Choose import directory", GLTFUtils.getPathAbsoluteFromProject(_importDirectory), GLTFUtils.getPathAbsoluteFromProject(_importDirectory));
                if (GLTFUtils.isFolderInProjectDirectory(newImportDir))
                {
                    _importDirectory = newImportDir;
                }
                else if (newImportDir != "")
                {
                    EditorUtility.DisplayDialog("Error", "Please select a path within your current Unity project (with Assets/)", "Ok");
                }
            }

            GUILayout.EndHorizontal();

            _ui.displaySubContent("Import into");
            GUILayout.BeginHorizontal();
            GUILayout.TextField(GLTFUtils.getPathProjectFromAbsolute(_importDirectory), GUILayout.MinWidth(UI_SIZE.x), GUILayout.Height(UI_SIZE.y));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Change destination", GUILayout.Height(UI_SIZE.y), GUILayout.Width(minWidthButton)))
            {
                string newImportDir = EditorUtility.OpenFolderPanel("Choose import directory", GLTFUtils.getPathAbsoluteFromProject(_importDirectory), GLTFUtils.getPathAbsoluteFromProject(_importDirectory));
                if (GLTFUtils.isFolderInProjectDirectory(newImportDir))
                {
                    _importDirectory = newImportDir;
                }
                else if (newImportDir != "")
                {
                    EditorUtility.DisplayDialog("Error", "Please select a path within your current Unity project (with Assets/)", "Ok");
                }
                else
                {
                    // Path is empty, user canceled. Do nothing
                }
            }
            GUILayout.EndHorizontal();

            // OPTIONS
            GUILayout.Space(2);
            _ui.displaySubContent("Options");
            GUILayout.BeginHorizontal();

            GUILayout.Label("Prefab name: ", GUILayout.Height(UI_SIZE.y));

            _prefabName = GUILayout.TextField(_prefabName, GUILayout.MinWidth(200), GUILayout.Height(UI_SIZE.y));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            _addToCurrentScene = GUILayout.Toggle(_addToCurrentScene, "Add to current scene");
            GUILayout.Space(2);

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            Color old = GUI.color;
            GUI.color = SketchfabUI.SKFB_BLUE;
            GUI.contentColor = Color.white;
            GUI.enabled = CanImport();
            if (GUILayout.Button("IMPORT", _ui.SketchfabButton))
            {
                processImportButton();
            }
            GUI.color = old;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            SketchfabPlugin.displayFooter();
        }

        private string stripProjectDirectory(string directory)
        {
            return directory.Replace(Application.dataPath, "Assets");
        }

        private bool CanImport()
        {
            return GLTFUtils.isFolderInProjectDirectory(_importDirectory) && File.Exists(_importFilePath);
        }

        private void emptyLines(int nbLines)
        {
            for (int i = 0; i < nbLines; ++i)
            {
                GUILayout.Label("");
            }
        }

        private void changeDirectory()
        {
            _importDirectory = EditorUtility.OpenFolderPanel("Choose import directory in Project", Application.dataPath, "Assets");

            // Discard if selected directory is outside of the project
            if (!isDirectoryInProject())
            {
                Debug.Log("Import directory is outside of project directory. Please select path in Assets/");
                _importDirectory = "";
                return;
            }
        }

        private void showOptions()
        {
            GUILayout.Label("Options", _header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prefab name:");
            _currentSampleName = GUILayout.TextField(_currentSampleName, GUILayout.Width(250));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            _addToCurrentScene = GUILayout.Toggle(_addToCurrentScene, "Add to current scene");
        }

        private bool isDirectoryInProject()
        {
            return _importDirectory.Contains(Application.dataPath);
        }

        public void loadFromBuffer(byte[] data)
        {
            if (!isDirectoryInProject())
            {
                Debug.LogError("Import directory is outside of project directory. Please select path in Assets/");
                return;
            }

            if (!Directory.Exists(_importDirectory))
            {
                Directory.CreateDirectory(_importDirectory);
            }

            _gltfPath = unzipGltfArchive(data);

            _importer.setupForPath(_gltfPath, _importDirectory, _currentSampleName, _addToCurrentScene);
            _importer.Load();
        }

        private void processImportButton()
        {
            if (!isDirectoryInProject())
            {
                Debug.LogError("Import directory is outside of project directory. Please select path in Assets/");
                return;
            }

            if (!Directory.Exists(_importDirectory))
            {
                Directory.CreateDirectory(_importDirectory);
            }

            if (Path.GetExtension(_importFilePath) == ".zip")
            {
                _gltfPath = unzipGltfArchive(_importFilePath);
            }

            _importer.setupForPath(_gltfPath, _importDirectory, _currentSampleName, _addToCurrentScene);
            _importer.Load();
        }

        private void OnFinishImport()
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Import successful", "Model has been successfully imported", "OK");
        }

        private void Update()
        {
            SketchfabPlugin.Update();
            if (_importer != null)
                _importer.Update();

        }

        public void UpdateProgress(UnityGLTF.GLTFEditorImporter.IMPORT_STEP step, int current, int total)
        {
            string element = "";
            switch (step)
            {
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.BUFFER:
                    element = "Buffer";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.IMAGE:
                    element = "Image";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.TEXTURE:
                    element = "Texture";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.MATERIAL:
                    element = "Material";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.MESH:
                    element = "Mesh";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.NODE:
                    element = "Node";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.ANIMATION:
                    element = "Animation";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.SKIN:
                    element = "Skin";
                    break;
            }

            EditorUtility.DisplayProgressBar("Importing glTF", "Importing " + element + " (" + current + " / " + total + ")", (float)current / (float)total);
            this.Repaint();
        }
    }
}

#endif