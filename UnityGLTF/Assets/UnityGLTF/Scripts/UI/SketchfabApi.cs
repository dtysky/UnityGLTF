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

namespace Sketchfab
{
	struct SketchfabUrls
	{
		public static string server = "https://sketchfab.com/";
		public static string latestRelease = "https://github.com/sketchfab/Unity-glTF-Exporter/releases";
		public static string resetPassword = "https://sketchfab.com/login/reset-password";
		public static string createAccount = "https://sketchfab.com/signup";
		public static string reportAnIssue = "https://help.sketchfab.com/hc/en-us/requests/new?type=exporters&subject=Unity+Exporter";
		public static string privateInfo = "https://help.sketchfab.com/hc/en-us/articles/115000422206-Private-Models";
		public static string draftInfo = "https://help.sketchfab.com/hc/en-us/articles/115000472906-Draft-Mode";
		public static string latestReleaseCheck = "https://api.github.com/repos/sketchfab/Unity-glTF-Exporter/releases";
		public static string plans = "https://sketchfab.com/plans";
		public static string categories = server + "/v3/categories";
		private static string dummyClientId = "IUO8d5VVOIUCzWQArQ3VuXfbwx5QekZfLeDlpOmW";
		public static string oauth = server + "oauth2/token/?grant_type=password&client_id=" + dummyClientId;
		public static string userMe = server + "/v3/me";
		public static string userAccount = server + "/v3/me/account";
		public static string postModel = server + "/v3/models";
		public static string modelUrl = server + "/models";
	}

	public enum ExporterState
	{
		IDLE,
		CHECK_VERSION,
		REQUEST_CODE,
		//GET_CATEGORIES,
		USER_ACCOUNT_TYPE,
		CAN_PRIVATE,
		PUBLISH_MODEL
	}

	public class SketchfabApi
	{
		public class SketchfabPlan
		{
			public string label;
			public int maxSize;

			public SketchfabPlan(string lb, int ms)
			{
				label = lb;
				maxSize = ms;
			}
		}

		public SketchfabPlan getPlan(string accountName)
		{
			switch (accountName)
			{
				case "pro":
					int nbPro = 200 * 1024 * 1024;
					return new SketchfabPlan("PRO", nbPro);
				case "prem":
					int nbPrem = 500 * 1024 * 1024;
					return new SketchfabPlan("PREMIUM", nbPrem);
				case "biz":
					int nbBiz = 500 * 1024 * 1024;
					return new SketchfabPlan("BUSINESS", nbBiz);
				case "ent":
					int nbEnt = 500 * 1024 * 1024;
					return new SketchfabPlan("ENTERPRISE", nbEnt);
			}

			int nbFree = 50 * 1024 * 1024;
			return new SketchfabPlan("BASIC", nbFree);
		}

		// Fields limits
		const int NAME_LIMIT = 48;
		const int DESC_LIMIT = 1024;
		const int TAGS_LIMIT = 50;
		const int PASSWORD_LIMIT = 64;
		const int SPACE_SIZE = 5;

		// Exporter objects and scripts
		public string _access_token = "";
		ExporterState _state;
		SketchfabRequest _publisher;
		public string _uploadSource = "Unity-exporter";

		//Dictionary<string, string> categories = new Dictionary<string, string>();
		//List<string> categoriesNames = new List<string>();
		private string _lastModelUrl;

		private bool _isUserPro;
		private string _userDisplayName;
		private SketchfabPlan _currentUserPlan = null;
		private bool _userCanPrivate = false;
		//int categoryIndex = 0;

		// Oauth stuff
		private float expiresIn = 0;
		private int lastTokenTime = 0;
		private string latestVersion;

		public delegate void Callback();
		private Callback _uploadSuccess;
		private Callback _uploadFailed;
		private Callback _tokenRequestSuccess;
		private Callback _tokenRequestFailed;
		private Callback _checkVersionSuccess;
		private Callback _checkVersionFailed;

		private Callback _checkUserAccountSuccess;
		private Callback _checkUserAccountFailure;

		private string _lastError = "";

		public SketchfabApi(string uploadSource)
		{
			_publisher = new SketchfabRequest(uploadSource);
			_publisher.setResponseCallback(HandleRequestResponse);
		}

		public bool isUserAuthenticated()
		{
			return _access_token.Length > 0;
		}

		//Setup callbacks
		public void setUploadSuccessCb(Callback callback)
		{
			_uploadSuccess = callback;
		}

		public void setUploadFailedCb(Callback callback)
		{
			_uploadFailed = callback;
		}

		public void setTokenRequestSuccessCb(Callback callback)
		{
			_tokenRequestSuccess = callback;
		}

		public void setTokenRequestFailedCb(Callback callback)
		{
			_tokenRequestFailed = callback;
		}

		public void setCheckVersionSuccessCb(Callback callback)
		{
			_checkVersionSuccess = callback;
		}

		public void setCheckVersionFailedCb(Callback callback)
		{
			_checkVersionFailed = callback;
		}

		public void setCheckUserAccountSuccessCb(Callback callback)
		{
			_checkUserAccountSuccess = callback;
		}

		public void setCheckUserAccountFailureCb(Callback callback)
		{
			_checkUserAccountFailure = callback;
		}

		public void logoutUser()
		{
			_access_token = "";
			_publisher.saveAccessToken(_access_token);
		}

		public void authenticateUser(string username, string password)
		{
			_state = ExporterState.REQUEST_CODE;
			_publisher.requestAccessToken(username, password);
		}

		public void checkLatestExporterVersion()
		{
			_state = ExporterState.CHECK_VERSION;
			_publisher.requestExporterReleaseInfo();
		}

		public void requestUserAccountInfo()
		{
			_state = ExporterState.USER_ACCOUNT_TYPE;
			_publisher.requestAccountInfo();
		}

		public void requestUserCanPrivate()
		{
			if(_currentUserPlan.label == "BASIC")
			{
				_userCanPrivate = false;
			}
			else
			{
				_state = ExporterState.CAN_PRIVATE;
				_publisher.requestUserCanPrivate();
			}
		}

		public bool getUserCanPrivate()
		{
			return _userCanPrivate;
		}

		public void publishModel(Dictionary<string, string> parameters, string zipPath)
		{
			_state = ExporterState.PUBLISH_MODEL;
			_publisher.postModel(parameters, zipPath);
		}

		int convertToSeconds(DateTime time)
		{
			return (int)(time.Hour * 3600 + time.Minute * 60 + time.Second);
		}

		public string getLatestVersion()
		{
			return latestVersion;
		}

		public string getLastError()
		{
			return _lastError;
		}

		public SketchfabPlan getUserPlan()
		{
			return _currentUserPlan;
		}

		public int getCurrentUserMaxAllowedUploadSize()
		{
			if (_currentUserPlan == null)
				return -1;

			return _currentUserPlan.maxSize;
		}

		public string getCurrentUserPlanLabel()
		{
			if (_currentUserPlan == null)
				return "Unknown";

			return _currentUserPlan.label;
		}

		public string getCurrentUserDisplayName()
		{
			if (_userDisplayName == null)
				return "";

			return _userDisplayName;
		}
		
		//void relog()
		//{
		//	if(publisher && publisher.getState() == ExporterState.REQUEST_CODE)
		//	{
		//		return;
		//	}
		//	if (user_name.Length == 0)
		//	{
		//		user_name = EditorPrefs.GetString(usernameEditorKey);
		//		//user_password = EditorPrefs.GetString(passwordEditorKey);
		//	}

		//	if (publisher && user_name.Length > 0 && user_password.Length > 0)
		//	{
		//		publisher.oauth(user_name, user_password);
		//	}
		//}

		private void checkAccessTokenValidity()
		{
			float currentTimeSecond = convertToSeconds(DateTime.Now);
			if (_access_token.Length > 0 && currentTimeSecond - lastTokenTime > expiresIn)
			{
				_access_token = "";
				//relog();
			}
		}

		public void HandleRequestResponse()
		{
			WWW www = _publisher.getResponse();

			if (www == null)
			{
				Debug.LogError("Request is empty (WWW object is null)");
				return;
			}

			JSONNode jsonResponse = parseResponse(www);
			switch (_state)
			{
				case ExporterState.CHECK_VERSION:
					if (jsonResponse != null && jsonResponse[0]["tag_name"] != null)
					{
						latestVersion = jsonResponse[0]["tag_name"];
						_checkVersionSuccess();
					}
					else
					{
						latestVersion = "";
						_checkVersionFailed();
					}
					break;
				case ExporterState.REQUEST_CODE:
					if (jsonResponse["access_token"] != null)
					{
						_access_token = jsonResponse["access_token"];
						expiresIn = jsonResponse["expires_in"].AsFloat;
						lastTokenTime = convertToSeconds(DateTime.Now);
						_publisher.saveAccessToken(_access_token);
						if (_tokenRequestSuccess != null)
							_tokenRequestSuccess();
					}
					else
					{
						if (_tokenRequestFailed != null)
							_tokenRequestFailed();
					}
					break;
				case ExporterState.PUBLISH_MODEL:
					if (www.responseHeaders["STATUS"].Contains("201") == true)
					{
						_lastModelUrl = SketchfabUrls.modelUrl + "/" + getUrlId(www.responseHeaders);
						if (_uploadSuccess != null)
							_uploadSuccess();
					}
					else
					{
						_lastError = www.responseHeaders["STATUS"];
						if (_uploadFailed != null)
							_uploadFailed();
					}
					break;
				//case ExporterState.GET_CATEGORIES:
				//	string jsonify = this.jsonify(www.text);
				//	if (!jsonify.Contains("results"))
				//	{
				//		Debug.Log(jsonify);
				//		Debug.Log("Failed to retrieve categories");
				//		publisher.setIdle();
				//		break;
				//	}

				//	JSONArray categoriesArray = JSON.Parse(jsonify)["results"].AsArray;
				//	foreach (JSONNode node in categoriesArray)
				//	{
				//		categories.Add(node["name"], node["slug"]);
				//		categoriesNames.Add(node["name"]);
				//	}
				//	setIdle();
				//	break;
				case ExporterState.USER_ACCOUNT_TYPE:
					string accountRequest = this.jsonify(www.text);
					if (!accountRequest.Contains("account"))
					{
						_lastError = "Failed to retrieve user account type";
						if (_checkUserAccountFailure != null)
							_checkUserAccountFailure();
						break;
					}
					else
					{
						var userSettings = JSON.Parse(accountRequest);
						string account = userSettings["account"];
						_currentUserPlan = getPlan(account);
						_userDisplayName = userSettings["displayName"];
						if (_checkUserAccountSuccess != null)
							_checkUserAccountSuccess();
					}
					break;
				case ExporterState.CAN_PRIVATE:
					string canPrivateRequest = this.jsonify(www.text);
					if (!canPrivateRequest.Contains("canProtectModels"))
					{
						Debug.Log("Failed to retrieve if user can private");
						setIdle();
						break;
					}
					_userCanPrivate = jsonResponse["canProtectModels"].AsBool;
					break;
			}
		}
		private void setIdle()
		{
			_state = ExporterState.IDLE;
		}

		private string getUrlId(Dictionary<string, string> responseHeaders)
		{
			return responseHeaders["LOCATION"].Split('/')[responseHeaders["LOCATION"].Split('/').Length - 1];
		}

		public string getModelUrl()
		{
			return _lastModelUrl;
		}

		private JSONNode parseResponse(WWW www)
		{
			return JSON.Parse(this.jsonify(www.text));
		}

		// Update is called once per frame
		public void Update()
		{
			checkAccessTokenValidity();
			_publisher.Update();
		}

		public float getUploadProgress()
		{
			if(_state == ExporterState.PUBLISH_MODEL && _publisher.getResponse() != null)
			{
				return 0.99f * _publisher.getResponse().uploadProgress + 0.01f * _publisher.getResponse().progress;
			}
			else
			{
				return -1.0f; // No upload in progress
			}
		}

		private string jsonify(string jsondata)
		{
			return jsondata.Replace("null", "\"null\"");
		}

		public string validateInputs(ref Dictionary<string, string> parameters)
		{
			string errors = "";

			if (parameters["name"].Length > NAME_LIMIT)
			{
				errors = "Model name is too long";
			}

			if (parameters["name"].Length == 0)
			{
				errors = "Please give a name to your model";
			}

			if (parameters["description"].Length > DESC_LIMIT)
			{
				errors = "Model description is too long";
			}


			if (parameters["tags"].Length > TAGS_LIMIT)
			{
				errors = "Model tags are too long";
			}

			return errors;
		}
	}
	class RequestManager
	{
		List<IEnumerator> _requests;
		IEnumerator _current = null;

		public RequestManager()
		{
				_requests = new List<IEnumerator>();
		}

		public void addTask(IEnumerator task)
		{
				_requests.Add(task);
		}

		public void clear()
		{
				_requests.Clear();
		}

		public bool play()
		{
			if (_requests.Count > 0)
			{
				if (_current == null || !_current.MoveNext())
				{
					_current = _requests[0];
					_requests.RemoveAt(0);
				}
			}

			if (_current != null)
				_current.MoveNext();

			if (_current != null && !_current.MoveNext() && _requests.Count == 0)
				return false;

			return true;
		}
	}


	public class SketchfabRequest
	{
		bool _isDone = false;
		public WWW www;
		private string access_token = "";
		private string uploadSource = "";
		public delegate void RequestResponseCallback();
		private RequestResponseCallback _callback;

		public SketchfabRequest(string source)
		{
			uploadSource = source;
		}

		public void saveAccessToken(string token)
		{
			access_token = token;
		}

		public void setResponseCallback(RequestResponseCallback responseCb)
		{
			_callback = responseCb;
		}

		public void Update()
		{
			if(!_isDone && www != null && www.isDone)
			{
				_isDone = true;
				_callback();
			}
		}

		// Request access_token
		public void requestAccessToken(string user_name, string user_password)
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			parameters.Add("username", user_name);
			parameters.Add("password", user_password);
			requestSketchfabAPI(SketchfabUrls.oauth, parameters);
		}

		public WWW getResponse()
		{
			return www;
		}

		public void requestExporterReleaseInfo()
		{
			requestSketchfabAPI(SketchfabUrls.latestReleaseCheck);
		}

		public void requestAccountInfo()
		{
			requestSketchfabAPI(SketchfabUrls.userMe);
		}

		public void requestUserCanPrivate()
		{
			requestSketchfabAPI(SketchfabUrls.userAccount);
		}

		public void postModel(Dictionary<string, string> parameters, string filePath)
		{
			if (!System.IO.File.Exists(filePath))
			{
				Debug.LogError("Exported file not found. Aborting");
				return;
			}

			byte[] data = File.ReadAllBytes(filePath);
			requestSketchfabAPI(SketchfabUrls.postModel, parameters, data, filePath);
		}

		public void requestSketchfabAPI(string url)
		{
			_isDone = false;
			if (access_token.Length > 0)
			{
				WWWForm postForm = new WWWForm();
				Dictionary<string, string> headers = postForm.headers;
				if (access_token.Length > 0)
					headers["Authorization"] = "Bearer " + access_token;

				www = new WWW(url, null, headers);
			}
			else
			{
				www = new WWW(url);
			}
		}

		public void requestSketchfabAPI(string url, Dictionary<string, string> parameters)
		{
			_isDone = false;
			WWWForm postForm = new WWWForm();


			// Set parameters
			foreach (string param in parameters.Keys)
			{
				postForm.AddField(param, parameters[param]);
			}

			// Create and send request
			if(access_token.Length > 0 )
			{
				Dictionary<string, string> headers = postForm.headers;
				if (access_token.Length > 0)
					headers["Authorization"] = "Bearer " + access_token;

				www = new WWW(url, postForm.data, headers);
			}
			else
			{
				www = new WWW(url, postForm);
			}
			
		}

		public void requestSketchfabAPI(string url, Dictionary<string, string> parameters, byte[] data, string fileName = "")
		{
			_isDone = false;
			WWWForm postForm = new WWWForm();
			// Set parameters
			foreach (string param in parameters.Keys)
			{
				postForm.AddField(param, parameters[param]);
			}
			
			// Add source
			postForm.AddField("source", uploadSource);

			// add data
			if (data.Length > 0)
			{
				postForm.AddBinaryData("modelFile", data, fileName, "application/zip");
			}

			Dictionary<string, string> headers = postForm.headers;
			headers["Authorization"] = "Bearer " + access_token;
		
			// Create and send request
			www = new WWW(url, postForm.data, headers);
		}
	}
}
#endif