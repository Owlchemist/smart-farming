using HarmonyLib;
using Verse;
using RimWorld;
using static SmartFarming.Mod_SmartFarming;

namespace SmartFarming
{
    //Zone spawn
    [HarmonyPatch (typeof(Zone), nameof(Zone.PostRegister))]
    static class Patch_PostRegister
    {
        static void Postfix(Zone __instance)
        {
			if (__instance is Zone_Growing)
			{
                var growZoneRegistry = compCache[__instance.Map].growZoneRegistry;
			    if (!growZoneRegistry.ContainsKey(__instance.ID)) growZoneRegistry.Add(__instance.ID, new ZoneData());
			}
        }
    }

    //Zone delete
    [HarmonyPatch (typeof(Zone), nameof(Zone.Deregister))]
    static class Patch_Deregister
    {
        static void Prefix(Zone __instance)
        {
			if (__instance is Zone_Growing)
			{
				var growZoneRegistry = compCache[__instance.Map].growZoneRegistry;
				if (growZoneRegistry.ContainsKey(__instance.ID)) growZoneRegistry.Remove(__instance.ID);
			}
        }
    }

    //Change plant type
	[HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.SetPlantDefToGrow))]
    static class Patch_SetPlantDefToGrow
    {
        static void Postfix(Zone_Growing __instance)
        {
			compCache[__instance.Map].CalculateAll(__instance);
        }
    }

    //Zone expand
	[HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.AddCell))]
    static class Patch_AddCell
    {
        static void Postfix(Zone_Growing __instance)
        {
			compCache[__instance.Map].CalculateAll(__instance);
        }
    }

    //Zone shrink
	[HarmonyPatch (typeof(Zone), nameof(Zone.RemoveCell))]
    static class Patch_RemoveCell
    {
        static void Postfix(Zone __instance)
        {
			if (__instance is Zone_Growing && __instance.cells.Count > 0) compCache[__instance.Map].CalculateAll((Zone_Growing)__instance);
        }
    }

    //Flush the cache on reload
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
	public class Patch_LoadGame
	{
        static void Prefix()
        {
            compCache.Clear();
        }
    }
}