Shader "UI/ChromaKey" {
    // Keys out a green screen from a UI texture (e.g. a VideoPlayer RenderTexture on a RawImage).
    // Alpha is driven by how strongly green dominates red/blue, so white/black/blue/yellow art is
    // kept intact. A despill step pulls leftover green fringe on the edges back toward neutral.
    Properties {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _KeyThreshold ("Key Threshold", Range(0,1)) = 0.20   // green dominance where cutout starts
        _KeySoftness  ("Key Softness",  Range(0.001,0.5)) = 0.12  // soft edge width
        _Despill      ("Despill",       Range(0,1)) = 1.0    // how much edge green to remove
        _Color ("Tint", Color) = (1,1,1,1)

        // standard UI stencil/mask plumbing
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader {
        Tags {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil {
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

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                float4 world  : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _KeyThreshold;
            float _KeySoftness;
            float _Despill;
            float4 _ClipRect;

            v2f vert (appdata v) {
                v2f o;
                o.world = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.uv);

                // green dominance: how much the green channel exceeds the brighter of red/blue
                float other = max(c.r, c.b);
                float greenness = c.g - other;

                // 1 where clearly green -> transparent, 0 where clearly not green -> opaque.
                // Set alpha directly instead of multiplying: a VideoPlayer RenderTexture often
                // carries alpha 0, which would make the whole dog invisible.
                float key = smoothstep(_KeyThreshold, _KeyThreshold + _KeySoftness, greenness);
                c.a = 1.0 - key;

                // despill: clamp leftover green toward the neutral channels on kept edges
                c.g = lerp(c.g, min(c.g, other), _Despill);

                c *= i.color;
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                clip(c.a - 0.001);
                return c;
            }
            ENDCG
        }
    }
}
