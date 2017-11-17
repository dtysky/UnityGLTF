
Shader "GLTF/ExtractMetalSmooth" {
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

			 struct vertInput {
			 float4 pos : POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 struct vertOutput {
			 float4 pos : SV_POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 sampler2D _MetallicGlossMap;
			 sampler2D _OcclusionMap;
			 int _FlipY;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 if (_FlipY == 1)
					 o.texcoord.y = 1.0 - input.texcoord.y;
				 else
					 o.texcoord.y = input.texcoord.y;

				 return o;
			 }

			 half4 frag(vertOutput output) : COLOR {
			 	half4 final = half4(0.0, 0.0, 0.0 ,1.0);
			 	final.r = tex2D(_OcclusionMap, output.texcoord).r;
				final.g = 1.0f - tex2D(_MetallicGlossMap, output.texcoord).a;
				final.b = tex2D(_MetallicGlossMap, output.texcoord).r;

			 	final.a = 1.0f;

			 	return final;
			 }

			ENDCG
		}
	}
}
