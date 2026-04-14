using System.Collections.ObjectModel;

namespace WoWRenderLib.Renderer
{
    public static class ShaderEnums
    {
        public enum WMOVertexShader : int
        {
            None = -1,
            MapObjDiffuse_T1 = 0,
            MapObjDiffuse_T1_Refl = 1,
            MapObjDiffuse_T1_Env_T2 = 2,
            MapObjSpecular_T1 = 3,
            MapObjDiffuse_Comp = 4,
            MapObjDiffuse_Comp_Refl = 5,
            MapObjDiffuse_Comp_Terrain = 6,
            MapObjDiffuse_CompAlpha = 7,
            MapObjParallax = 8
        }

        public enum WMOPixelShader : int
        {
            None = -1,
            MapObjDiffuse = 0,
            MapObjSpecular = 1,
            MapObjMetal = 2,
            MapObjEnv = 3,
            MapObjOpaque = 4,
            MapObjEnvMetal = 5,
            MapObjTwoLayerDiffuse = 6,
            MapObjTwoLayerEnvMetal = 7,
            MapObjTwoLayerTerrain = 8,
            MapObjDiffuseEmissive = 9,
            MapObjMaskedEnvMetal = 10,
            MapObjEnvMetalEmissive = 11,
            MapObjTwoLayerDiffuseOpaque = 12,
            MapObjTwoLayerDiffuseEmissive = 13,
            MapObjAdditiveMaskedEnvMetal = 14,
            MapObjTwoLayerDiffuseMod2x = 15,
            MapObjTwoLayerDiffuseMod2xNA = 16,
            MapObjTwoLayerDiffuseAlpha = 17,
            MapObjLod = 18,
            MapObjParallax = 19,
            MapObjUnkShader = 20
        }


        public static readonly List<(WMOVertexShader VertexShader, WMOPixelShader PixelShader)> WMOShaders =
        [
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjDiffuse), // MapObjDiffuse 
            (WMOVertexShader.MapObjSpecular_T1, WMOPixelShader.MapObjSpecular), // MapObjSpecular
            (WMOVertexShader.MapObjSpecular_T1, WMOPixelShader.MapObjMetal), // MapObjMetal
            (WMOVertexShader.MapObjDiffuse_T1_Refl, WMOPixelShader.MapObjEnv), // MapObjEnv
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjOpaque), // MapObjOpaque
            (WMOVertexShader.MapObjDiffuse_T1_Refl, WMOPixelShader.MapObjEnvMetal), // MapObjEnvMetal
            (WMOVertexShader.MapObjDiffuse_Comp, WMOPixelShader.MapObjTwoLayerDiffuse), // MapObjTwoLayerDiffuse
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjTwoLayerEnvMetal), // MapObjTwoLayerEnvMetal
            (WMOVertexShader.MapObjDiffuse_Comp_Terrain, WMOPixelShader.MapObjTwoLayerTerrain), // MapObjTwoLayerTerrain
            (WMOVertexShader.MapObjDiffuse_Comp, WMOPixelShader.MapObjDiffuseEmissive), // MapObjDiffuseEmissive
            (WMOVertexShader.None, WMOPixelShader.None), // waterWindow
            (WMOVertexShader.MapObjDiffuse_T1_Env_T2, WMOPixelShader.MapObjMaskedEnvMetal), // MapObjMaskedEnvMetal
            (WMOVertexShader.MapObjDiffuse_T1_Env_T2, WMOPixelShader.MapObjEnvMetalEmissive), // MapObjEnvMetalEmissive
            (WMOVertexShader.MapObjDiffuse_Comp, WMOPixelShader.MapObjTwoLayerDiffuseOpaque), // MapObjTwoLayerDiffuseOpaque
            (WMOVertexShader.None, WMOPixelShader.None), // submarineWindow
            (WMOVertexShader.MapObjDiffuse_Comp, WMOPixelShader.MapObjTwoLayerDiffuseEmissive), // MapObjTwoLayerDiffuseEmissive
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjDiffuse), // MapObjDiffuseTerrain
            (WMOVertexShader.MapObjDiffuse_T1_Env_T2, WMOPixelShader.MapObjAdditiveMaskedEnvMetal), // MapObjAdditiveMaskedEnvMetal
            (WMOVertexShader.MapObjDiffuse_CompAlpha, WMOPixelShader.MapObjTwoLayerDiffuseMod2x), // MapObjTwoLayerDiffuseMod2x
            (WMOVertexShader.MapObjDiffuse_Comp, WMOPixelShader.MapObjTwoLayerDiffuseMod2xNA), // MapObjTwoLayerDiffuseMod2xNA
            (WMOVertexShader.MapObjDiffuse_CompAlpha, WMOPixelShader.MapObjTwoLayerDiffuseAlpha), // MapObjTwoLayerDiffuseAlpha
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjLod), // MapObjLod
            (WMOVertexShader.MapObjParallax, WMOPixelShader.MapObjParallax), // MapObjParallax
            (WMOVertexShader.MapObjDiffuse_T1, WMOPixelShader.MapObjUnkShader) // MapObjUnkShader
        ];

        // M2, based on work by Deamon: https://github.com/Deamon87/WebWowViewerCpp/blob/master/wowViewerLib/src/engine/objects/m2/m2Object.cpp#L13

        public enum M2VertexShader : int
        {
            Diffuse_T1 = 0,
            Diffuse_Env = 1,
            Diffuse_T1_T2 = 2,
            Diffuse_T1_Env = 3,
            Diffuse_Env_T1 = 4,
            Diffuse_Env_Env = 5,
            Diffuse_T1_Env_T1 = 6,
            Diffuse_T1_T1 = 7,
            Diffuse_T1_T1_T1 = 8,
            Diffuse_EdgeFade_T1 = 9,
            Diffuse_T2 = 10,
            Diffuse_T1_Env_T2 = 11,
            Diffuse_EdgeFade_T1_T2 = 12,
            Diffuse_EdgeFade_Env = 13,
            Diffuse_T1_T2_T1 = 14,
            Diffuse_T1_T2_T3 = 15,
            Color_T1_T2_T3 = 16,
            BW_Diffuse_T1 = 17,
            BW_Diffuse_T1_T2 = 18,
        };

        public enum M2PixelShader : int
        {
            // WotLK deprecated shaders
            Combiners_Decal = -1,
            Combiners_Add = -2,
            Combiners_Mod2x = -3,
            Combiners_Fade = -4,
            Combiners_Opaque_Add = -5,
            Combiners_Opaque_AddNA = -6,
            Combiners_Add_Mod = -7,
            Combiners_Mod2x_Mod2x = -8,

            // Legion modern shaders
            Combiners_Opaque = 0,
            Combiners_Mod = 1,
            Combiners_Opaque_Mod = 2,
            Combiners_Opaque_Mod2x = 3,
            Combiners_Opaque_Mod2xNA = 4,
            Combiners_Opaque_Opaque = 5,
            Combiners_Mod_Mod = 6,
            Combiners_Mod_Mod2x = 7,
            Combiners_Mod_Add = 8,
            Combiners_Mod_Mod2xNA = 9,
            Combiners_Mod_AddNA = 10,
            Combiners_Mod_Opaque = 11,
            Combiners_Opaque_Mod2xNA_Alpha = 12,
            Combiners_Opaque_AddAlpha = 13,
            Combiners_Opaque_AddAlpha_Alpha = 14,
            Combiners_Opaque_Mod2xNA_Alpha_Add = 15,
            Combiners_Mod_AddAlpha = 16,
            Combiners_Mod_AddAlpha_Alpha = 17,
            Combiners_Opaque_Alpha_Alpha = 18,
            Combiners_Opaque_Mod2xNA_Alpha_3s = 19,
            Combiners_Opaque_AddAlpha_Wgt = 20,
            Combiners_Mod_Add_Alpha = 21,
            Combiners_Opaque_ModNA_Alpha = 22,
            Combiners_Mod_AddAlpha_Wgt = 23,
            Combiners_Opaque_Mod_Add_Wgt = 24,
            Combiners_Opaque_Mod2xNA_Alpha_UnshAlpha = 25,
            Combiners_Mod_Dual_Crossfade = 26,
            Combiners_Opaque_Mod2xNA_Alpha_Alpha = 27,
            Combiners_Mod_Masked_Dual_Crossfade = 28,
            Combiners_Opaque_Alpha = 29,
            Guild = 30,
            Guild_NoBorder = 31,
            Guild_Opaque = 32,
            Combiners_Mod_Depth = 33,
            Illum = 34,
            Combiners_Mod_Mod_Mod_Const = 35,
            Combiners_Mod_Mod_Depth = 36
        };

        public static readonly List<(M2PixelShader PixelShader, M2VertexShader VertexShader, int HullShader, int DomainShader)> M2Shaders =
        [
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_AddAlpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_AddAlpha_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha_Add, M2VertexShader.Diffuse_T1_Env_T1, 2, 2 ),
            (M2PixelShader.Combiners_Mod_AddAlpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_AddAlpha, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Mod_AddAlpha, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Mod_AddAlpha_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Alpha_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha_3s, M2VertexShader.Diffuse_T1_Env_T1, 2, 2 ),
            (M2PixelShader.Combiners_Opaque_AddAlpha_Wgt, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Mod_Add_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_ModNA_Alpha, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Mod_AddAlpha_Wgt, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Mod_AddAlpha_Wgt, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_AddAlpha_Wgt, M2VertexShader.Diffuse_T1_T2, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Mod_Add_Wgt, M2VertexShader.Diffuse_T1_Env, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha_UnshAlpha, M2VertexShader.Diffuse_T1_Env_T1, 2, 2 ),
            (M2PixelShader.Combiners_Mod_Dual_Crossfade, M2VertexShader.Diffuse_T1, 0, 0 ),
            (M2PixelShader.Combiners_Mod_Depth, M2VertexShader.Diffuse_EdgeFade_T1, 0, 0 ),
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha_Alpha, M2VertexShader.Diffuse_T1_Env_T2, 2, 2 ),
            (M2PixelShader.Combiners_Mod_Mod, M2VertexShader.Diffuse_EdgeFade_T1_T2, 1, 1 ),
            (M2PixelShader.Combiners_Mod_Masked_Dual_Crossfade, M2VertexShader.Diffuse_T1_T2, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Alpha, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Opaque_Mod2xNA_Alpha_UnshAlpha, M2VertexShader.Diffuse_T1_Env_T2, 2, 2 ),
            (M2PixelShader.Combiners_Mod_Depth, M2VertexShader.Diffuse_EdgeFade_Env, 0, 0 ),
            (M2PixelShader.Guild, M2VertexShader.Diffuse_T1_T2_T1, 2, 1 ),
            (M2PixelShader.Guild_NoBorder, M2VertexShader.Diffuse_T1_T2, 1, 2 ),
            (M2PixelShader.Guild_Opaque, M2VertexShader.Diffuse_T1_T2_T1, 2, 1 ),
            (M2PixelShader.Illum, M2VertexShader.Diffuse_T1_T1, 1, 1 ),
            (M2PixelShader.Combiners_Mod_Mod_Mod_Const, M2VertexShader.Diffuse_T1_T2_T3, 2, 2 ),
            (M2PixelShader.Combiners_Mod_Mod_Mod_Const, M2VertexShader.Color_T1_T2_T3, 2, 2 ),
            (M2PixelShader.Combiners_Opaque, M2VertexShader.Diffuse_T1, 0, 0 ),
            (M2PixelShader.Combiners_Mod_Mod2x, M2VertexShader.Diffuse_EdgeFade_T1_T2, 1, 1 ),
            (M2PixelShader.Combiners_Mod, M2VertexShader.Diffuse_EdgeFade_T1, 1, 1 ),
            (M2PixelShader.Combiners_Mod_Mod_Depth, M2VertexShader.Diffuse_EdgeFade_T1_T2, 1, 1 ),
        ];
    }
}
