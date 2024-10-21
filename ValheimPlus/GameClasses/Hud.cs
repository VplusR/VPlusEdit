using HarmonyLib;
using JetBrains.Annotations;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    [HarmonyPatch(typeof(Hud), nameof(Hud.DamageFlash))]
    public static class Hud_DamageFlash_Patch
    {
        [UsedImplicitly]
        private static void Postfix(Hud __instance)
        {
            var config = Configuration.Current.Hud;
            if (!config.IsEnabled || !config.removeDamageFlash) return;
            
            __instance.m_damageScreen.gameObject.SetActive(false);
        }
    }
}