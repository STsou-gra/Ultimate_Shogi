// 画面の色の反転（超常魔法「停止世界」の時間停止中の見た目。TimeStopView が画面を覆うスプライトに使う）。
// それより奥に描かれたもの（同じ並び順の層で sortingOrder が小さいもの）の色を反転する。
// 頂点色のアルファで反転の度合いを変える（0 = そのまま、1 = 完全に反転）：
//   出力 = src × (1 − dst) + dst × (1 − a)、src = (a, a, a) → (1 − a) × dst + a × (1 − dst)
Shader "UltimateShogi/InvertScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend OneMinusDstColor OneMinusSrcAlpha

        // LightMode を付けない（SRPDefaultUnlit）。2D Renderer・Universal Renderer のどちらでも 1 回だけ描かれる
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a;
                return half4(a, a, a, a);
            }
            ENDHLSL
        }
    }
}
