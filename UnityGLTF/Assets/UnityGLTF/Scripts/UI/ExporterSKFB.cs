#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using System.Runtime.Serialization.Formatters.Binary;
using System;
using UnityEditor.SceneManagement;
using UnityGLTF;

public class ExporterSKFB : EditorWindow
{

	[MenuItem("Sketchfab/Publish to Sketchfab %_f")]
	static void Init()
	{
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX // edit: added Platform Dependent Compilation - win or osx standalone
		ExporterSKFB window = (ExporterSKFB)EditorWindow.GetWindow(typeof(ExporterSKFB));
		window.titleContent.text = "Sketchfab";
		window.Show();
#else // and error dialog if not standalone
		EditorUtility.DisplayDialog("Error", "Your build target must be set to standalone", "Okay");
#endif
	}

	// UI dimensions (to be cleaned)
	[SerializeField]
	Vector2 loginSize = new Vector2(603, 190);
	[SerializeField]
	Vector2 fullSize = new Vector2(603, 690);
	[SerializeField]
	Vector2 descSize = new Vector2(603, 175);

	// Fields limits
	const int NAME_LIMIT = 48;
	const int DESC_LIMIT = 1024;
	const int TAGS_LIMIT = 50;
	const int PASSWORD_LIMIT = 64;
	const int SPACE_SIZE = 5;

	private string exporterVersion = GLTFSceneExporter.EXPORTER_VERSION;
	private string latestVersion = "0.0.1";

	// Exporter UI: static elements
	[SerializeField]
	Texture2D header;
	GUIStyle exporterTextArea;
	GUIStyle exporterLabel;
	GUIStyle exporterClickableLabel;
	private string clickableLabelColor = "navy";

	Sketchfab.SketchfabApi _api;
	private string exportPath;
	private string zipPath;

	// Login 
	private string user_name = "";
	private string user_password = "";
	const string usernameEditorKey = "UnityExporter_username";

	// Upload params and options
	private bool opt_exportAnimation = true;
	private bool opt_exportSelection = false;
	private string param_name = "";
	private string param_description = "";
	private string param_tags = "";
	private bool param_autopublish = true;
	private bool param_private = false;
	private string param_password = "";

	// Exporter UI: dynamic elements
	private string status = "";
	private Color blueColor = new Color(69 / 255.0f, 185 / 255.0f, 223 / 255.0f);
	private Color redColor = new Color(0.8f, 0.0f, 0.0f);
	private Color greyColor = Color.white;
	// Disabled 
	//Dictionary<string, string> categories = new Dictionary<string, string>();
	//List<string> categoriesNames = new List<string>();
	Rect windowRect;

	private bool isLatestVersion = true;
	private bool checkVersionFailed = false;

	//private List<String> tagList;
	void Awake()
	{
		zipPath = Application.temporaryCachePath + "/" + "Unity2Skfb.zip";
		exportPath = Application.temporaryCachePath + "/" + "Unity2Skfb.gltf";
		resizeWindow(loginSize);
	}

	void setupApi()
	{
		_api = new Sketchfab.SketchfabApi("Unity-exporter");

		//Setup callbacks
		_api.setTokenRequestFailedCb(OnAuthenticationFail);
		_api.setTokenRequestSuccessCb(OnAuthenticationSuccess);
		_api.setCheckVersionSuccessCb(OnCheckVersionSuccess);
		_api.setCheckUserAccountSuccessCb(OnCheckUserAccountSuccess);
		_api.setUploadSuccessCb(OnUploadSuccess);
		_api.setUploadFailedCb(OnUploadFailed);
		_api.checkLatestExporterVersion();
	}
	void OnEnable()
	{
		// Pre-fill model name with scene name if empty
		if (param_name.Length == 0)
		{
			param_name = EditorSceneManager.GetActiveScene().name;
		}
		resizeWindow(loginSize);
		relog();
	}

	int convertToSeconds(DateTime time)
	{
		return (int)(time.Hour * 3600 + time.Minute * 60 + time.Second);
	}

	void OnUploadSuccess()
	{
		Application.OpenURL(_api.getModelUrl());
	}

	void OnUploadFailed()
	{

	}

	void OnSelectionChange()
	{
		// do nothing for now
	}

	void OnAuthenticationFail()
	{
		EditorUtility.DisplayDialog("Error", "Authentication failed: invalid email and/or password \n" + _api.getLastError(), "Ok");
	}

	void OnAuthenticationSuccess()
	{
		_api.requestUserAccountInfo();
	}

	void OnCheckUserAccountSuccess()
	{
		_api.requestUserCanPrivate();
	}

	void OnCheckVersionSuccess()
	{
		if (exporterVersion == _api.getLatestVersion())
		{
			isLatestVersion = true;
		}
		else
		{
			bool update = EditorUtility.DisplayDialog("Exporter update", "A new version is available \n(you have version " + exporterVersion + ")\nIt's strongly rsecommended that you update now. The latest version may include important bug fixes and improvements", "Update", "Skip");
			if (update)
			{
				Application.OpenURL(Sketchfab.SketchfabUrls.latestRelease);
			}
			isLatestVersion = false;
		}
	}

	void OnCheckVersionFailed()
	{
		checkVersionFailed = true;
	}

	void resizeWindow(Vector2 size)
	{
		//this.maxSize = size;
		this.minSize = size;
	}

	void relog()
	{
		if (user_name.Length == 0)
		{
			user_name = EditorPrefs.GetString(usernameEditorKey);
			//user_password = EditorPrefs.GetString(passwordEditorKey);
		}

		if (user_name.Length > 0 && user_password.Length > 0)
		{
			_api.authenticateUser(user_name, user_password);
		}
	}

	void expandWindow(bool expand)
	{
		windowRect = this.position;
		windowRect.height = expand ? fullSize.y : loginSize.y;
		position = windowRect;
	}

	private bool updateExporterStatus()
	{
		status = "";

		if (param_name.Length > NAME_LIMIT)
		{
			status = "Model name is too long";
			return false;
		}


		if (param_name.Length == 0)
		{
			status = "Please give a name to your model";
			return false;
		}


		if (param_description.Length > DESC_LIMIT)
		{
			status = "Model description is too long";
			return false;
		}


		if (param_tags.Length > TAGS_LIMIT)
		{
			status = "Model tags are too long";
			return false;
		}


		int nbSelectedObjects = Selection.GetTransforms(SelectionMode.Deep).Length;
		if (nbSelectedObjects == 0)
		{
			status = "No object selected to export";
			return false;
		}

		status = "Upload " + nbSelectedObjects + " object" + (nbSelectedObjects != 1 ? "s" : "");
		return true;
	}

	private void checkValidity()
	{
		if (exporterLabel == null)
		{
			exporterLabel = new GUIStyle(GUI.skin.label);
			exporterLabel.richText = true;
		}

		if (exporterTextArea == null)
		{
			exporterTextArea = new GUIStyle(GUI.skin.textArea);
			exporterTextArea.fixedWidth = descSize.x;
			exporterTextArea.fixedHeight = descSize.y;
		}

		if (exporterClickableLabel == null)
		{
			exporterClickableLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
			exporterClickableLabel.richText = true;
		}

		if(_api == null)
		{
			setupApi();
		}
	}

	private string makeTextRed(string text)
	{
		return "<color=" + clickableLabelColor + ">" + text + "</color>";
	}

	private void Update()
	{
		if (_api != null)
		{
			_api.Update();
		}
	}

	void OnGUI()
	{
		checkValidity();
		//Header
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		GUILayout.Label(header);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		// Account settings
		if (!_api.isUserAuthenticated())
		{
			showLoginUi();
		}
		else
		{
			if (checkVersionFailed)
			{
				showVersionCheckError();
			}
			else if (isLatestVersion)
			{
				showUpToDate();
			}
			else
			{
				showOutdatedVersionWarning();
			}

			GUILayout.BeginHorizontal("Box");
			GUILayout.Label("Account: <b>" + _api.getCurrentUserDisplayName() + ( _api.getCurrentUserPlanLabel().Length > 0 ? "</b> (" + _api.getCurrentUserPlanLabel() + " account)" : ""), exporterLabel);
			if (GUILayout.Button("Logout"))
			{
				_api.logoutUser();
				resizeWindow(loginSize);
			}
			GUILayout.EndHorizontal();
		}

		GUILayout.Space(SPACE_SIZE);

		if (_api.isUserAuthenticated())
		{
			showModelProperties();
			GUILayout.Space(SPACE_SIZE);
			showPrivateSetting();
			showOptions();

			bool enable = updateExporterStatus();
			if (enable)
				GUI.color = blueColor;
			else
				GUI.color = greyColor;

			if (_api.getUploadProgress() >= 0.0f)
			{
				Rect r = EditorGUILayout.BeginVertical();
				EditorGUI.ProgressBar(r, _api.getUploadProgress(), "Upload progress");
				GUILayout.Space(18);
				EditorGUILayout.EndVertical();
			}
			else
			{
				GUI.enabled = enable;
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();

				if (GUILayout.Button(status, GUILayout.Width(250), GUILayout.Height(40)))
				{
					if (!enable)
					{
						EditorUtility.DisplayDialog("Error", status, "Ok");
					}
					else
					{
						proceedToExportAndUpload();
					}
				}

				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void showVersionCheckError()
	{
		Color current = GUI.color;
		GUI.color = Color.red;
		GUILayout.Label("An error occured when looking for the latest exporter version\nYou might be using an old and not fully supported version", EditorStyles.centeredGreyMiniLabel);
		if (GUILayout.Button("Click here to be redirected to release page"))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.latestRelease);
		}
		GUI.color = current;
	}

	private void showOutdatedVersionWarning()
	{
		Color current = GUI.color;
		GUI.color = redColor;
		GUILayout.Label("New version " + latestVersion + " available (current version is " + exporterVersion + ")", EditorStyles.centeredGreyMiniLabel);
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("Go to release page", GUILayout.Width(150), GUILayout.Height(25)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.latestRelease);
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUI.color = current;
	}

	private void showUpToDate()
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label("Exporter is up to date (version:" + exporterVersion + ")", EditorStyles.centeredGreyMiniLabel);

		GUILayout.FlexibleSpace();
		if (GUILayout.Button("<color=" + clickableLabelColor + ">Help  -</color>", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.latestRelease);
		}

		if (GUILayout.Button("<color=" + clickableLabelColor + ">Report an issue</color>", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.reportAnIssue);
		}
		GUILayout.EndHorizontal();
	}

	private void showModelProperties()
	{
		// Model settings
		GUILayout.Label("Model properties", EditorStyles.boldLabel);

		// Model name
		GUILayout.Label("Name");
		param_name = EditorGUILayout.TextField(param_name);
		GUILayout.Label("(" + param_name.Length + "/" + NAME_LIMIT + ")", EditorStyles.centeredGreyMiniLabel);
		EditorStyles.textField.wordWrap = true;
		GUILayout.Space(SPACE_SIZE);

		GUILayout.Label("Description");
		param_description = EditorGUILayout.TextArea(param_description, exporterTextArea);
		GUILayout.Label("(" + param_description.Length + " / 1024)", EditorStyles.centeredGreyMiniLabel);
		GUILayout.Space(SPACE_SIZE);
		GUILayout.Label("Tags (separated by spaces)");
		param_tags = EditorGUILayout.TextField(param_tags);
		GUILayout.Label("'unity' and 'unity3D' added automatically (" + param_tags.Length + "/50)", EditorStyles.centeredGreyMiniLabel);
	}

	private void showPrivateSetting()
	{
		GUILayout.Label("Set the model to Private", EditorStyles.centeredGreyMiniLabel);
		if (_api.getUserCanPrivate())
		{
			EditorGUILayout.BeginVertical("Box");
			GUILayout.BeginHorizontal();
			param_private = EditorGUILayout.Toggle("Private model", param_private);

			if (GUILayout.Button("(<color=" + clickableLabelColor + ">more info</color>)", exporterClickableLabel, GUILayout.Height(20)))
			{
				Application.OpenURL(Sketchfab.SketchfabUrls.privateInfo);
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUI.enabled = param_private;
			GUILayout.Label("Password");
			param_password = EditorGUILayout.TextField(param_password);
			EditorGUILayout.EndVertical();
			GUI.enabled = true;
		}
		else
		{
			if (_api.getCurrentUserPlanLabel() == "BASIC")
			{
				if (GUILayout.Button("(<color=" + clickableLabelColor + ">Upgrade to a paid account to set your model to private</color>)", exporterClickableLabel, GUILayout.Height(20)))
				{
					Application.OpenURL(Sketchfab.SketchfabUrls.plans);
				}
			}
			else
			{
				if (GUILayout.Button("(<color=" + clickableLabelColor + ">You cannot set any other model to private (limit reached)</color>)", exporterClickableLabel, GUILayout.Height(20)))
				{
					Application.OpenURL(Sketchfab.SketchfabUrls.plans);
				}
			}
		}
	}

	private void showOptions()
	{
		GUILayout.Label("Options", EditorStyles.boldLabel);
		GUILayout.BeginHorizontal();
		opt_exportAnimation = EditorGUILayout.Toggle("Export animation (beta)", opt_exportAnimation);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		opt_exportSelection = EditorGUILayout.Toggle("Export selection", opt_exportSelection);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		param_autopublish = EditorGUILayout.Toggle("Publish immediately ", param_autopublish);
		if (GUILayout.Button("(<color=" + clickableLabelColor + ">more info</color>)", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.latestRelease);
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		//GUILayout.Space(SPACE_SIZE);

		//if (categories.Count > 0)
		//	categoryIndex = EditorGUILayout.Popup(categoryIndex, categoriesNames.ToArray());

		//GUILayout.Space(SPACE_SIZE);
	}

	private void proceedToExportAndUpload()
	{
		if (System.IO.File.Exists(zipPath))
		{
			System.IO.File.Delete(zipPath);
		}

		// "Sketchfab Plugin (Unity " + Application.unityVersion + ")"
		var exporter = new GLTFSceneExporter(opt_exportSelection ? GLTFUtils.getSelectedTransforms() : GLTFUtils.getSceneTransforms());
		exporter.enableAnimation(opt_exportAnimation);
		exporter.SaveGLTFandBin(Path.GetDirectoryName(exportPath), Path.GetFileNameWithoutExtension(exportPath));

		GLTFUtils.buildZip(exporter.getExportedFilesList(), Path.Combine(Path.GetDirectoryName(exportPath), "Unity2Skfb.zip"), true);
		if (File.Exists(zipPath))
		{
			bool shouldUpload = checkFileSize(zipPath);

			if (!shouldUpload)
			{
				shouldUpload = EditorUtility.DisplayDialog("Error", "The export exceed the max file size allowed by your current account type", "Continue", "Cancel");
			}
			_api.publishModel(buildParameterDict(), zipPath);
		}
		else
		{
			Debug.Log("Zip file has not been generated. Aborting publish.");
		}
	}

	private void showLoginUi()
	{
		GUILayout.Label("Log in with your Sketchfab account", EditorStyles.centeredGreyMiniLabel);

		user_name = EditorGUILayout.TextField("Email", user_name);
		user_password = EditorGUILayout.PasswordField("Password", user_password);

		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("<color=" + clickableLabelColor + ">Create an account  - </color>", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.createAccount);
		}
		if (GUILayout.Button("<color=" + clickableLabelColor + ">Reset your password  - </color>", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.resetPassword);
		}
		if (GUILayout.Button("<color=" + clickableLabelColor + ">Report an issue</color>", exporterClickableLabel, GUILayout.Height(20)))
		{
			Application.OpenURL(Sketchfab.SketchfabUrls.reportAnIssue);
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();

		if (GUILayout.Button("Login", GUILayout.Width(150), GUILayout.Height(25)))
		{
			_api.authenticateUser(user_name, user_password);
			EditorPrefs.SetString(usernameEditorKey, user_name);
		}

		GUILayout.EndHorizontal();
	}

	private bool checkFileSize(string zipPath)
	{
		FileInfo file = new FileInfo(zipPath);
		status = "Uploading " + file.Length / (1024.0f * 1024.0f);
		return file.Length < _api.getCurrentUserMaxAllowedUploadSize();
	}

	private Dictionary<string, string> buildParameterDict()
	{
		Dictionary<string, string> parameters = new Dictionary<string, string>();
		parameters["name"] = param_name;
		parameters["description"] = param_description;
		parameters["tags"] = "unity unity3D " + param_tags;
		parameters["private"] = param_private ? "1" : "0";
		parameters["isPublished"] = param_autopublish ? "1" : "0";
		//string category = categories[categoriesNames[categoryIndex]];
		//Debug.Log(category);
		//parameters["categories"] = category;
		if (param_private)
			parameters["password"] = param_password;

		return parameters;
	}

	void OnDestroy()
	{
		if (System.IO.File.Exists(zipPath))
			System.IO.File.Delete(zipPath);
	}
}

#endif