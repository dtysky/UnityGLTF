using System;
using System.Collections;
using UnityEngine;
using System.Threading;
using UnityEngine.Networking;

namespace GLTF {

	public class GLTFComponent : MonoBehaviour
	{
		public string Url;
		public string importDirectory;
		public bool saveInProject;
		public bool Multithreaded = true;

		public int MaximumLod = 300;

		public Shader GLTFStandard;
		public Shader GLTFConstant;

		public void Start()
		{
			var loader = new GLTFFileLoader(
				Url,
				gameObject.transform,
				saveInProject, 
				importDirectory
			);

			loader.SetShaderForMaterialType(GLTFFileLoader.MaterialType.PbrMetallicRoughness, GLTFStandard);
			loader.SetShaderForMaterialType(GLTFFileLoader.MaterialType.CommonConstant, GLTFConstant);
			loader.Multithreaded = Multithreaded;
			loader.MaximumLod = MaximumLod;
			loader.Load();
		}
	}
}
