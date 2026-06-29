// 텍스처 구동 헥스 지형 셰이더 (URP / Unlit)
//  - _TerrainTex : 셀별 지형 인덱스(R, Point/Clamp). 프래그먼트에서 point 샘플 → 칼 같은 헥스 경계
//  - _PaletteTex : 인덱스→색 (가로로 색 나열, Point/Clamp)
//  - _HeightTex  : 셀별 고도(R, Point/Clamp). 버텍스에서 샘플해 y로 변위 → y축 확장용
// 색은 전적으로 텍스처가 담당하므로, 페인팅 시 메시를 다시 만들 필요가 없다(픽셀 1개만 갱신).
Shader "Custom/HexTerrainTextured"
{
    Properties
    {
        _TerrainTex   ("Terrain Index (R)", 2D) = "black" {}
        _PaletteTex   ("Palette", 2D)           = "white" {}
        _HeightTex    ("Height (R)", 2D)        = "black" {}
        _PaletteWidth ("Palette Width", Float)  = 5
        _HeightScale  ("Height Scale", Float)   = 0      // y축 추가 전엔 0(평평). 추후 키우면 고도 적용
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TerrainTex); SAMPLER(sampler_TerrainTex);
            TEXTURE2D(_PaletteTex); SAMPLER(sampler_PaletteTex);
            TEXTURE2D(_HeightTex);  SAMPLER(sampler_HeightTex);

            CBUFFER_START(UnityPerMaterial)
                float _PaletteWidth;
                float _HeightScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // 셀 텍셀 중심 좌표 (셀 전체가 동일)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                // 고도 텍스처를 읽어 y로 변위 (point 샘플 → 셀마다 평평한 단(plateau))
                float h = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, IN.uv, 0).r;
                float3 p = IN.positionOS.xyz;
                p.y += h * _HeightScale;

                OUT.positionHCS = TransformObjectToHClip(p);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 셀의 지형 인덱스를 point 샘플 → 경계가 보간되지 않아 칼같이 떨어진다
                float idx = SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, IN.uv).r;

                // 팔레트에서 해당 인덱스 색을 뽑는다 (텍셀 중심을 정확히 찍기 위해 +0.5)
                float u = (idx + 0.5) / max(_PaletteWidth, 1.0);
                half4 col = SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(u, 0.5));
                return col;
            }
            ENDHLSL
        }
    }
}
