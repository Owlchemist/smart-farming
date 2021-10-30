using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using static SmartFarming.Mod_SmartFarming;

namespace GrowingZonePriorities
{
	//This tells the workgiver that growing is a priority-enabled job
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.Prioritized), MethodType.Getter)]
	public static class Patch_WorkGiver_Scanner
	{
		public static bool Prefix(ref bool __result, ref WorkGiver_Scanner __instance)
		{
			if (!(__instance is WorkGiver_Grower)) return true;

			__result = true;
			return false;
		}
	}

	//This returns the priority value (higher = more urgent)
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.GetPriority), new Type[] { typeof(Pawn), typeof(TargetInfo) })]
	public static class GetPriorityPatcher
	{
		public static void Postfix(Pawn pawn, TargetInfo t, ref float __result, WorkGiver_Scanner __instance)
		{
			if (!(__instance is WorkGiver_Grower)) return;

			var zone = pawn.Map.zoneManager.ZoneAt(t.Cell) as Zone_Growing;
			if (zone == null) 
			{
				__result = 2f; //This would be a hydroponic
				return;
			}
			
			var comp = compCache.GetValueSafe(pawn.Map);
			var zoneData = comp?.growZoneRegistry.GetValueSafe(zone.ID);

			__result = (float)zoneData?.priority;
		}
	}

	//This color-codes the selection edge borders to priority
	[HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionBracketFor))]
	public static class Patch_DrawSelectionBracketFor
	{
		public static bool Prefix(object obj)
		{
			Zone zone = obj as Zone_Growing;
			if (zone != null)
			{
				var comp = compCache.GetValueSafe(zone.Map);
				var zoneData = comp?.growZoneRegistry.GetValueSafe(zone.ID);
				if (zoneData == null) return true;

				Color color = Color.white;
				if (zoneData.priority == SmartFarming.ZoneData.Priority.Low) color = Color.grey;
				else if (zoneData.priority == SmartFarming.ZoneData.Priority.Preferred) color = Color.green;
				else if (zoneData.priority == SmartFarming.ZoneData.Priority.Important) color = Color.yellow;
				else if (zoneData.priority == SmartFarming.ZoneData.Priority.Critical) color = Color.red;
				
				GenDraw.DrawFieldEdges(zone.Cells, color, null);
				return false;
			}
			return true;
		}
	}
}