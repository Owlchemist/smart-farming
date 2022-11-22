using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using static SmartFarming.Mod_SmartFarming;

namespace SmartFarming
{
	//This tells the workgiver that growing is a priority-enabled job
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.Prioritized), MethodType.Getter)]
	public static class Patch_WorkGiver_Scanner_Prioritized
	{
		public static bool Postfix(bool __result, WorkGiver_Scanner __instance)
		{
			return __instance.def == ResourceBank.WorkGiverDefOf.GrowerHarvest;
		}
	}

	//This returns the priority value (higher = more urgent)
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.GetPriority), new Type[] { typeof(Pawn), typeof(TargetInfo) })]
	public static class Patch_WorkGiver_Scanner_GetPriority
	{
		public static float Postfix(float __result, Pawn pawn, TargetInfo t, WorkGiver_Scanner __instance)
		{
			if (__instance.def != ResourceBank.WorkGiverDefOf.GrowerHarvest) return __result;

			Map map = pawn.Map;
			var zone = map.zoneManager.zoneGrid[t.cellInt.z * map.info.sizeInt.x + t.cellInt.x] as Zone_Growing;
			
			if (zone == null) 
			{
				return 2f; //This would be a hydroponic
			}

			return (float)compCache.TryGetValue(map.uniqueID)?.growZoneRegistry.TryGetValue(zone.ID)?.priority;
		}
	}

	//This color-codes the selection edge borders to priority
	[HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionBracketFor))]
	public static class Patch_DrawSelectionBracketFor
	{
		public static bool Prefix(object obj)
		{
			Zone zone = obj as Zone_Growing;
			if (zone != null && (compCache.TryGetValue(zone.Map?.uniqueID ?? -1)?.growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData) ?? false))
			{
				Color color;
				switch (zoneData.priority)
				{
					case SmartFarming.ZoneData.Priority.Low: {
						color = ResourceBank.grey; break;
					}
					case SmartFarming.ZoneData.Priority.Preferred: {
						color = ResourceBank.green; break;
					}
					case SmartFarming.ZoneData.Priority.Important: {
						color = ResourceBank.yellow; break;
					}
					case SmartFarming.ZoneData.Priority.Critical: {
						color = ResourceBank.red; break;
					}
					default: {
						color = ResourceBank.white;
						break;
					}
				}
				
				GenDraw.DrawFieldEdges(zone.Cells, color, null);
				return false;
			}
			return true;
		}
	}
}