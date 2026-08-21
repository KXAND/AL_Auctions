Shader "UI/BoardReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BorderWidth ("Border Width", Float) = 1
        _Dashed ("Dashed", Float) = 0
        _DashLength ("Dash Length", Float) = 8
        _HatchSpacing ("Hatch Spacing", Float) = 12
        _HatchWidth ("Hatch Width", Float) = 2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _BorderWidth;
            float _Dashed;
            float _DashLength;
            float _HatchSpacing;
            float _HatchWidth;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uvPixels = min(input.texcoord, 1.0 - input.texcoord) /
                                  max(fwidth(input.texcoord), float2(0.0001, 0.0001));
                float edgeDistance = min(uvPixels.x, uvPixels.y);
                float border = step(edgeDistance, _BorderWidth);

                float horizontalDash = step(
                    frac(input.vertex.x / max(_DashLength, 1.0)),
                    0.55);
                float verticalDash = step(
                    frac(input.vertex.y / max(_DashLength, 1.0)),
                    0.55);
                float dash = uvPixels.y < uvPixels.x ? horizontalDash : verticalDash;
                border *= lerp(1.0, dash, saturate(_Dashed));

                float hatchPosition = input.vertex.x - input.vertex.y;
                float hatch = step(
                    frac(hatchPosition / max(_HatchSpacing, 1.0)),
                    _HatchWidth / max(_HatchSpacing, 1.0));

                float shape = saturate(border + hatch);
                fixed4 color = input.color;
                color.a *= shape;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
