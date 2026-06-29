// 빅토리아3식 텍스처 구동 헥스 맵 셰이더 (URP / Unlit)
//
// 핵심: 표면은 평면 쿼드 1장. 프래그먼트마다 월드 위치 → "어느 헥스 셀인지"를 좌표 역산으로 계산해
//       지형/프로빈스 텍스처를 point 샘플한다. 셀마다 메시를 만들 필요가 없다.
//  - _TerrainTex     : 셀별 지형 인덱스(R, Point/Clamp)
//  - _TerrainPalette : 지형 인덱스 → 색
//  - _ProvinceTex    : 셀별 (프로빈스 인덱스 + 1). 0 = 프로빈스 없음(R, Point/Clamp)
//  - _ProvincePalette: 프로빈스 인덱스 → 표시색
//  - 국경선: 인접 위치의 프로빈스 ID와 비교해 다르면 선을 그림(셰이더 실시간 → 칠하면 자동 갱신)
Shader "Custom/HexTerrainProvince"
{
    Properties
    {
        _TerrainTex      ("Terrain Index (R)", 2D) = "black" {}
        _TerrainPalette  ("Terrain Palette", 2D)   = "white" {}
        _ProvinceTex     ("Province Id+1 (R)", 2D)  = "black" {}
        _ProvincePalette ("Province Palette", 2D)   = "white" {}

        _GridW ("Grid Width", Float) = 8
        _GridH ("Grid Height", Float) = 6
        _InnerR ("Hex Inner Radius", Float) = 8.66025404
        _OuterR ("Hex Outer Radius", Float) = 10
        _TerrainPaletteW ("Terrain Palette W", Float) = 5
        _ProvincePaletteW ("Province Palette W", Float) = 2

        _ProvinceTint ("Province Tint", Range(0,1)) = 0.75
        _BorderColor ("Border Color", Color) = (0.05,0.04,0.03,1)
        _BorderWidth ("Border Width (world)", Float) = 1.5
        [Toggle] _ShowBorders ("Show Borders", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TerrainTex);      SAMPLER(sampler_TerrainTex);
            TEXTURE2D(_TerrainPalette);  SAMPLER(sampler_TerrainPalette);
            TEXTURE2D(_ProvinceTex);     SAMPLER(sampler_ProvinceTex);
            TEXTURE2D(_ProvincePalette); SAMPLER(sampler_ProvincePalette);

            CBUFFER_START(UnityPerMaterial)
                float _GridW, _GridH, _InnerR, _OuterR;
                float _TerrainPaletteW, _ProvincePaletteW;
                float _ProvinceTint, _BorderWidth, _ShowBorders;
                float4 _BorderColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 posOS : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.posOS = IN.positionOS.xyz; // 격자 로컬 좌표(셀 배치와 같은 공간)
                return OUT;
            }

            // 월드(로컬) xz → 셀 오프셋 좌표 (HexCoordinates.FromPosition 포팅, 큐브 라운딩)
            int2 CellOf(float2 p)
            {
                float fx = p.x / (_InnerR * 2.0);
                float fy = -fx;
                float off = p.y / (_OuterR * 3.0);   // p.y = 로컬 z
                fx -= off; fy -= off;
                float fz = -fx - fy;

                int iX = (int)round(fx);
                int iY = (int)round(fy);
                int iZ = (int)round(fz);

                float dX = abs(fx - iX);
                float dY = abs(fy - iY);
                float dZ = abs(fz - iZ);
                if (dX > dY && dX > dZ) iX = -iY - iZ;
                else if (dZ > dY)       iZ = -iX - iY;

                int offX = iX + (int)floor(iZ * 0.5);
                return int2(offX, iZ);
            }

            bool InMap(int2 c) { return c.x >= 0 && c.x < (int)_GridW && c.y >= 0 && c.y < (int)_GridH; }

            float2 CellUV(int2 c) { return float2((c.x + 0.5) / _GridW, (c.y + 0.5) / _GridH); }

            float TerrainAt(int2 c)
            { return SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, CellUV(c)).r; }

            float ProvinceAt(int2 c)
            {
                if (!InMap(c)) return 0.0;
                return SAMPLE_TEXTURE2D(_ProvinceTex, sampler_ProvinceTex, CellUV(c)).r; // 0=없음, else idx+1
            }

            half4 PaletteColor(TEXTURE2D_PARAM(tex, smp), float idx, float width)
            {
                float u = (idx + 0.5) / max(width, 1.0);
                return SAMPLE_TEXTURE2D(tex, smp, float2(u, 0.5));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 p = IN.posOS.xz;
                int2 cell = CellOf(p);
                int2 cc = int2(clamp(cell.x, 0, (int)_GridW - 1), clamp(cell.y, 0, (int)_GridH - 1));

                float tIdx = TerrainAt(cc);
                half4 terrainCol = PaletteColor(TEXTURE2D_ARGS(_TerrainPalette, sampler_TerrainPalette), tIdx, _TerrainPaletteW);

                float pid = ProvinceAt(cc); // 0=없음
                half4 baseCol = terrainCol;
                if (pid > 0.5)
                {
                    half4 provCol = PaletteColor(TEXTURE2D_ARGS(_ProvincePalette, sampler_ProvincePalette), pid - 1.0, _ProvincePaletteW);
                    baseCol = lerp(terrainCol, provCol, _ProvinceTint);
                }

                // 국경선: 프로빈스끼리(둘 다 있음)만 긋는다. "프로빈스 없음(0)"과는 긋지 않아
                // green 지형 옆에 검은 띠가 생기지 않는다.
                if (_ShowBorders > 0.5 && pid > 0.5)
                {
                    float edge = 0.0;
                    const float k = 0.8660254; // sin60
                    float2 dirs[6] = {
                        float2( _BorderWidth, 0),
                        float2(-_BorderWidth, 0),
                        float2( _BorderWidth*0.5,  _BorderWidth*k),
                        float2(-_BorderWidth*0.5,  _BorderWidth*k),
                        float2( _BorderWidth*0.5, -_BorderWidth*k),
                        float2(-_BorderWidth*0.5, -_BorderWidth*k)
                    };
                    [unroll]
                    for (int i = 0; i < 6; i++)
                    {
                        float np = ProvinceAt(CellOf(p + dirs[i]));
                        if (np > 0.5 && abs(np - pid) > 0.5) edge = 1.0; // 다른 "프로빈스"일 때만
                    }
                    baseCol = lerp(baseCol, _BorderColor, edge);
                }

                return baseCol;
            }
            ENDHLSL
        }
    }
}
