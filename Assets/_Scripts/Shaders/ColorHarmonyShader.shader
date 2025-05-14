Shader "Custom/ColorHarmony" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        
        [Header(Color Harmony)]
        _HarmonyType ("Harmony Type", Range(0, 4)) = 0 // 0=Analogous, 1=Complementary, 2=Triadic, 3=Tetradic, 4=Monochromatic
        _BaseHue ("Base Hue", Range(0, 1)) = 0.0
        _HueShift ("Hue Shift", Range(-0.5, 0.5)) = 0.0
        _Saturation ("Saturation", Range(0, 2)) = 1.0
        _SaturationBalance ("Saturation Balance", Range(0, 1)) = 0.5
        _Brightness ("Brightness", Range(0, 2)) = 1.0
        _Contrast ("Contrast", Range(0, 2)) = 1.0
        
        [Header(Color Grading)]
        _Vibrance ("Vibrance", Range(-1, 1)) = 0.0
        _ColorTemperature ("Color Temperature", Range(-1, 1)) = 0.0
        _Tint ("Tint", Range(-1, 1)) = 0.0
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
            float _HarmonyType;
            float _BaseHue;
            float _HueShift;
            float _Saturation;
            float _SaturationBalance;
            float _Brightness;
            float _Contrast;
            float _Vibrance;
            float _ColorTemperature;
            float _Tint;
            
            // Convert RGB to HSV
            float3 rgb2hsv(float3 c) {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            
            // Convert HSV to RGB
            float3 hsv2rgb(float3 c) {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }
            
            // Apply color temperature (blue-orange shift)
            float3 applyColorTemperature(float3 color, float temperature) {
                // Warm (positive values) adds orange, cool (negative values) adds blue
                float3 warm = float3(0.1, 0.05, -0.15); // Orange shift
                float3 cool = float3(-0.1, -0.05, 0.15); // Blue shift
                
                if (temperature > 0) {
                    return color + warm * temperature;
                } else {
                    return color + cool * abs(temperature);
                }
            }
            
            // Apply tint (green-magenta shift)
            float3 applyTint(float3 color, float tint) {
                // Magenta (positive values), green (negative values)
                float3 magenta = float3(0.1, -0.1, 0.1);
                float3 green = float3(-0.1, 0.1, -0.1);
                
                if (tint > 0) {
                    return color + magenta * tint;
                } else {
                    return color + green * abs(tint);
                }
            }
            
            // Apply vibrance (intelligent saturation)
            float3 applyVibrance(float3 color, float vibrance) {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                float saturation = max(max(color.r, color.g), color.b) - min(min(color.r, color.g), color.b);
                float3 direction = color - luma;
                
                // Reduce saturation for already saturated colors, increase for unsaturated
                float saturationFactor = (1.0 - saturation) * vibrance;
                
                return luma + direction * (1.0 + saturationFactor);
            }
            
            // Get harmony hue based on base hue and harmony type
            float3 getHarmonyHues(float baseHue, float harmonyType) {
                float3 hues = float3(baseHue, 0, 0);
                
                // Analogous: hues 30° apart
                if (harmonyType < 1.0) {
                    hues.y = frac(baseHue + 1.0/12.0);
                    hues.z = frac(baseHue - 1.0/12.0);
                }
                // Complementary: hues 180° apart
                else if (harmonyType < 2.0) {
                    hues.y = frac(baseHue + 0.5);
                    hues.z = baseHue;
                }
                // Triadic: hues 120° apart
                else if (harmonyType < 3.0) {
                    hues.y = frac(baseHue + 1.0/3.0);
                    hues.z = frac(baseHue + 2.0/3.0);
                }
                // Tetradic: two complementary pairs
                else if (harmonyType < 4.0) {
                    hues.y = frac(baseHue + 0.25);
                    hues.z = frac(baseHue + 0.5);
                }
                // Monochromatic: same hue, different saturation/value
                else {
                    hues.y = baseHue;
                    hues.z = baseHue;
                }
                
                return hues;
            }
            
            // Apply color harmony to a pixel
            float3 applyColorHarmony(float3 color, float baseHue, float harmonyType, float hueShift, float satBalance) {
                // Convert to HSV
                float3 hsv = rgb2hsv(color);
                
                // Get harmony hues
                float3 harmonyHues = getHarmonyHues(baseHue, harmonyType);
                
                // Shift the base hue
                harmonyHues += hueShift;
                
                // Find the closest harmony hue to the current pixel hue
                float dist1 = min(abs(hsv.x - harmonyHues.x), min(abs(hsv.x - harmonyHues.x + 1), abs(hsv.x - harmonyHues.x - 1)));
                float dist2 = min(abs(hsv.x - harmonyHues.y), min(abs(hsv.x - harmonyHues.y + 1), abs(hsv.x - harmonyHues.y - 1)));
                float dist3 = min(abs(hsv.x - harmonyHues.z), min(abs(hsv.x - harmonyHues.z + 1), abs(hsv.x - harmonyHues.z - 1)));
                
                float targetHue;
                if (dist1 <= dist2 && dist1 <= dist3) {
                    targetHue = harmonyHues.x;
                } else if (dist2 <= dist1 && dist2 <= dist3) {
                    targetHue = harmonyHues.y;
                } else {
                    targetHue = harmonyHues.z;
                }
                
                // Blend between original hue and harmony hue based on saturation
                // More saturated colors are shifted more towards the harmony hues
                float blendFactor = hsv.y * satBalance;
                hsv.x = lerp(hsv.x, targetHue, blendFactor);
                
                // Convert back to RGB
                return hsv2rgb(hsv);
            }
            
            fixed4 frag (v2f i) : SV_Target {
                // Sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Apply color harmony
                col.rgb = applyColorHarmony(col.rgb, _BaseHue, _HarmonyType, _HueShift, _SaturationBalance);
                
                // Apply saturation
                float3 hsv = rgb2hsv(col.rgb);
                hsv.y *= _Saturation;
                col.rgb = hsv2rgb(hsv);
                
                // Apply vibrance (intelligent saturation)
                col.rgb = applyVibrance(col.rgb, _Vibrance);
                
                // Apply brightness and contrast
                col.rgb = (col.rgb - 0.5) * _Contrast + 0.5;
                col.rgb *= _Brightness;
                
                // Apply color temperature and tint
                col.rgb = applyColorTemperature(col.rgb, _ColorTemperature);
                col.rgb = applyTint(col.rgb, _Tint);
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
