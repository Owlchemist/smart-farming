using HarmonyLib;
using Verse;
using RimWorld;
using static SmartFarming.Mod_SmartFarming;
using static SmartFarming.MapComponent_SmartFarming;

namespace SmartFarming
{   
    [HarmonyPatch (typeof(Zone), nameof(Zone.PostRegister))]
    static class Patch_PostRegister
    {
        static void Postfix(Zone __instance)
        {
			if (__instance is Zone_Growing)
			{
				var growZoneRegistry = compCache[__instance.Map].growZoneRegistry;
				if (!growZoneRegistry.ContainsKey(__instance.ID)) growZoneRegistry.Add(__instance.ID, new ZoneData());

                //Run this on the next tick because RimWorld handles zone registration before it lets map components initialize
				LongEventHandler.QueueLongEvent(() => compCache[__instance.Map].CalculateAll((Zone_Growing)__instance), "CalculateAll", false, null);
			}
        }
    }
    [HarmonyPatch (typeof(Zone), nameof(Zone.PostDeregister))]
    static class PostDeregister
    {
        static void Prefix(Zone __instance)
        {
			if (__instance is Zone_Growing)
			{
				var growZoneRegistry = compCache[__instance.Map].growZoneRegistry;
				if (!growZoneRegistry.ContainsKey(__instance.ID)) growZoneRegistry.Remove(__instance.ID);
			}
        }
    }

	[HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.SetPlantDefToGrow))]
    static class Patch_SetPlantDefToGrow
    {
        static void Postfix(Zone_Growing __instance)
        {
			compCache[__instance.Map].CalculateAll(__instance);
        }
    }

	[HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.AddCell))]
    static class Patch_AddCell
    {
        static void Postfix(Zone_Growing __instance)
        {
			compCache[__instance.Map].CalculateAll(__instance);
        }
    }
	[HarmonyPatch (typeof(Zone), nameof(Zone.RemoveCell))]
    static class Patch_RemoveCell
    {
        static void Postfix(Zone __instance)
        {
			if (__instance is Zone_Growing) compCache[__instance.Map].CalculateAll((Zone_Growing)__instance);
        }
    }
}