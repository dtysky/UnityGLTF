
Shader "GLTF/ExtractOcclusion" {
	Properties{
		_OcclusionMetallicRoughMap("Texture", 2D) = "white" {}
		_FlipY("Flip texture Y", Int) = 0
	}

	SubShader {
		 Pass {
			 CGPROGRAM

			 #pragma vertex vert
			 #pragma fragment frag
			 #include "UnityCG.cginc"
			#include "GLTFColorSpace.cginc"

			 struct vertInput {
			 float4 pos : POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 struct vertOutput {
			 float4 pos : SV_POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 sampler2D _OcclusionMetallicRoughMap;
			 int _FlipY;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 o.texcoord = input.texcoord;

				 return o;
			 }

			 float4 frag(vertOutput output) : COLOR {
			 	float4 final = float4(1.0, 1.0, 1.0 ,1.0);
			 	final.rgb = tex2D(_OcclusionMetallicRoughMap, output.texcoord).rrr;
			 	final.a = 1.0f;

			 	return final;
			 }

			ENDCG
		}
	}
}
