using HarmonyLib;
using Traits;

namespace GameplayMusic
{
    [HarmonyPatch(typeof(Minion), "Summon")]
    internal static class SummonHook
    {
        private static void Postfix()
        { if (MusicPlayer.Instance != null) MusicPlayer.Instance.OnSummon(); }
    }

    [HarmonyPatch(typeof(Traits.Plagued), "OnAddTrait")]
    internal static class PlagueHook
    {
        private static void Postfix(ITraitable __0)
        { if (__0 is Character && MusicPlayer.Instance != null) MusicPlayer.Instance.OnDisaster("plague infection"); }
    }

    [HarmonyPatch(typeof(BurningSource), "AddObjectOnFire")]
    internal static class FireHook
    {
        private static void Prefix(BurningSource __instance, out int __state)
        { __state = __instance.objectsOnFire == null ? 0 : __instance.objectsOnFire.Count; }
        private static void Postfix(BurningSource __instance, int __state)
        {
            if (MusicPlayer.Instance != null && __instance.objectsOnFire != null &&
                __instance.objectsOnFire.Count > __state && __instance.objectsOnFire.Count >= 3)
                MusicPlayer.Instance.OnDisaster("spreading fire");
        }
    }
}
