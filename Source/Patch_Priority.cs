using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using static SmartFarming.Mod_SmartFarming;

namespace SmartFarming
{
	//This tells the workgiver that growing is a priority-enabled job
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.Prioritized), MethodType.Getter)]
	public static class Patch_WorkGiver_Scanner_Prioritized
	{
		public static bool Postfix(bool __result, WorkGiver_Scanner __instance)
		{
			return agriWorkTypes.Contains(__instance.def.index);
		}
	}

	//This returns the priority value (higher = more urgent)
	[HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.GetPriority), new Type[] { typeof(Pawn), typeof(TargetInfo) })]
	public static class Patch_WorkGiver_Scanner_GetPriority
	{
		public static float Postfix(float __result, Pawn pawn, TargetInfo t, WorkGiver_Scanner __instance)
		{
			if (!agriWorkTypes.Contains(__instance.def.index)) return __result;

			Map map = pawn.Map;
			var zone = map.zoneManager.zoneGrid[t.cellInt.z * map.info.sizeInt.x + t.cellInt.x] as Zone_Growing;
			
			if (zone == null) 
			{
				return 2f; //This would be a hydroponic
			}

			if (compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming mapComp) && mapComp.growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData))
			{
				return (float)zoneData.priority;
			}
			return __result;
		}
	}

	//This color-codes the selection edge borders to priority
	[HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionBracketFor))]
	public static class Patch_DrawSelectionBracketFor
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
			bool ran = false;
			bool skipOriginal = false;
			foreach (var code in instructions)
			{
				if (ran && !skipOriginal)
				{
					skipOriginal = true;
					continue;
				}
				yield return code;
				if (!ran && code.opcode == OpCodes.Callvirt && code.OperandIs(AccessTools.Property(typeof(Zone), nameof(Zone.Cells)).GetGetMethod()))
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
					yield return new CodeInstruction(OpCodes.Call, typeof(MapComponent_SmartFarming).GetMethod(nameof(MapComponent_SmartFarming.DrawFieldEdges)));

                    ran = true;
                }
			}
			if (!ran) Log.Warning("[Smart Farming] Transpiler could not find target for field edge drawer. There may be a mod conflict, or RimWorld updated?");
        }
	}
}