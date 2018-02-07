/*
 * Copyright(c) 2017-2018 Sketchfab Inc.
 * License: https://github.com/sketchfab/UnityGLTF/blob/master/LICENSE
 */
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityGLTF;
using Ionic.Zip;

/// <summary>
/// Class to handle imports from Sketchfab
/// </summary>
class SketchfabImporter
{
	string _currentSampleName = "Imported";

    GLTFEditorImporter _importer;
	private List<string> _unzippedFiles;

    // Settings
    string _unzipDirectory = Application.temporaryCachePath + "/unzip";
    string _importDirectory = Application.dataPath + "/Import";
    bool _addToCurrentScene = false;

    string _gltfInput;

    public SketchfabImporter(GLTFEditorImporter.ProgressCallback progressCallback, GLTFEditorImporter.RefreshWindow finishCallback)
    {
        _importer = new GLTFEditorImporter(progressCallback, finishCallback);
        _unzippedFiles = new List<string>();
    }

	private string findGltfFile(string directory)
	{
		string gltfFile = "";
		DirectoryInfo info = new DirectoryInfo(directory);
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

    private void deleteExistingGLTF()
    {
        string gltfFile = findGltfFile(_unzipDirectory);
        if (gltfFile != "")
        {
            Debug.Log("GLTF file found, and should be cleaned :" + gltfFile);
            File.Delete(gltfFile);
        }
    }

    public void cleanArtifacts()
    {
        GLTFUtils.removeFileList(_unzippedFiles.ToArray());
    }

    public void configure(string importDirectory, string prefabName, bool addToScene=false)
    {

        if (importDirectory.Length > 0)
        {
            if (!GLTFUtils.isFolderInProjectDirectory(importDirectory))
            {
                Debug.LogError("Import directory in not in Assets");
            }
            else
            {
                _importDirectory = importDirectory;
            }
        }

        if (prefabName.Length > 0)
            _currentSampleName = prefabName;

        _addToCurrentScene = addToScene;
    }

	private string unzipGltfArchive(string zipPath)
	{
        if (!Directory.Exists(_unzipDirectory))
            Directory.CreateDirectory(_unzipDirectory);
        else
            deleteExistingGLTF();

		// Extract archive
		ZipFile zipfile = ZipFile.Read(zipPath);

        foreach (ZipEntry e in zipfile)
        {
            // check if you want to extract e or not
            _unzippedFiles.Add(_unzipDirectory + "/" + e.FileName);
            e.Extract(_unzipDirectory, ExtractExistingFileAction.OverwriteSilently);
        }


		return findGltfFile(_unzipDirectory);
	}

    private string unzipGLTFArchiveData(byte[] zipData)
    {
        if (!Directory.Exists(_unzipDirectory))
            Directory.CreateDirectory(_unzipDirectory);
        else
            deleteExistingGLTF();

        MemoryStream stream = new MemoryStream(zipData);
        ZipFile zipfile = ZipFile.Read(stream);
        foreach (ZipEntry e in zipfile)
        {
            // check if you want to extract e or not
            _unzippedFiles.Add(_unzipDirectory + "/" + e.FileName);
            e.Extract(_unzipDirectory, ExtractExistingFileAction.OverwriteSilently);
        }

        return findGltfFile(_unzipDirectory);
    }

	public void OnDestroy()
	{
		GLTFUtils.removeFileList(_unzippedFiles.ToArray());
		GLTFUtils.removeEmptyDirectory(_unzipDirectory);
	}

	private string stripProjectDirectory(string directory)
	{
		return directory.Replace(Application.dataPath, "Assets");
	}

    public void loadFromBuffer(byte[] data)
    {
        if (!GLTFUtils.isFolderInProjectDirectory(_importDirectory))
        {
            Debug.LogError("Import directory is outside of project directory. Please select path in Assets/");
            return;
        }

        if (!Directory.Exists(_importDirectory))
        {
            Directory.CreateDirectory(_importDirectory);
        }

        _gltfInput = unzipGLTFArchiveData(data);
        _importer.setupForPath(_gltfInput, _importDirectory, _currentSampleName, _addToCurrentScene);
        _importer.Load();
    }

	private void loadFromFile(string filepath)
	{
        _gltfInput = filepath;

        if (Path.GetExtension(filepath) == ".zip")
		{
            _gltfInput = unzipGltfArchive(filepath);
		}

		_importer.setupForPath(_gltfInput, _importDirectory, _currentSampleName, _addToCurrentScene);
		_importer.Load();
	}

    public void Update()
    {
        _importer.Update();
    }
}
#endif