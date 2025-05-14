Shader "Custom/TiltShift" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurSize ("Blur Size", Range(0.0, 10.0)) = 1.0
        _FocusPosition ("Focus Position", Range(0.0, 1.0)) = 0.5
        _FocusSize ("Focus Size", Range(0.0, 1.0)) = 0.1
        _Feathering ("Feathering", Range(0.001, 1.0)) = 0.1
    }
    
    SubShader {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            sampler2D _MainTex;
            float _BlurSize;
            float _FocusPosition;
            float _FocusSize;
            float _Feathering;
            float4 _MainTex_TexelSize;
            
            // Helper function to calculate blur amount based on distance from focus area
            float getBlurAmount(float pos) {
                float focusStart = _FocusPosition - _FocusSize * 0.5;
                float focusEnd = _FocusPosition + _FocusSize * 0.5;
                
                // If inside focus area, no blur
                if (pos >= focusStart && pos <= focusEnd)
                    return 0.0;
                
                // Calculate blur based on distance from focus area with feathering
                float distFromFocus;
                
                if (pos < focusStart) {
                    distFromFocus = (focusStart - pos) / _Feathering;
                } else {
                    distFromFocus = (pos - focusEnd) / _Feathering;
                }
                
                return saturate(distFromFocus) * _BlurSize;
            }
            
            // Simple gaussian blur
            float4 gaussianBlur(float2 uv, float blurAmount) {
                if (blurAmount <= 0)
                    return tex2D(_MainTex, uv);
                
                float4 color = float4(0, 0, 0, 0);
                float total = 0;
                
                // Sample pattern for a simple gaussian blur
                // We use a variable number of samples based on blur amount
                int samples = min(16, max(4, blurAmount * 4));
                
                for (int x = -samples/2; x <= samples/2; x++) {
                    for (int y = -samples/2; y <= samples/2; y++) {
                        float2 offset = float2(x, y) * _MainTex_TexelSize.xy * blurAmount;
                        float weight = 1.0 / (1.0 + x*x + y*y); // Simple weight function
                        color += tex2D(_MainTex, uv + offset) * weight;
                        total += weight;
                    }
                }
                
                return color / total;
            }
            
            fixed4 frag (v2f i) : SV_Target {
                // Calculate blur amount based on vertical position
                float blurAmount = getBlurAmount(i.uv.y);
                
                // Apply gaussian blur
                fixed4 col = gaussianBlur(i.uv, blurAmount);
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
