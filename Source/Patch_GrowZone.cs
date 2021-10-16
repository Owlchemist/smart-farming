using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using static SmartFarming.Mod_SmartFarming;
using static SmartFarming.ModSettings_SmartFarming;
using static SmartFarming.ZoneData;

namespace SmartFarming
{
    //This replaces the vanilla sow button with our own.
    [HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos))]
    static class Patch_GetGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Zone_Growing __instance)
        {
             foreach (var value in values)
            {
                Command command = value as Command;
                if (command != null && command.hotKey == KeyBindingDefOf.Command_ItemForbid) continue;
                yield return value;
            }

            var comp = compCache[__instance.Map];
            if (comp != null)
            {
                ZoneData zoneData = comp.growZoneRegistry[__instance.ID];
                
                string label = "SmartFarming" + zoneData.sowMode.ToString();
                string desc = "SmartFarming" + zoneData.sowMode.ToString() + "Desc";

                if (Find.Selector.NumSelected == 1 || CheckIfZonesEqual(comp))
                {
                    yield return new Command_Action()
                    {
                        defaultLabel = label.Translate(),
                        defaultDesc = desc.Translate(),
                        hotKey = KeyBindingDefOf.Command_ItemForbid,
                        icon = zoneData.iconCache,
                        action = () => comp.SwitchSowMode(__instance)
                    };
                }

                yield return new Command_Toggle
                {
                    defaultLabel = "SmartFarming_NoPettyJobs".Translate(),
                    defaultDesc = "SmartFarming_NoPettyJobsDesc".Translate(),
                    icon = TexCommand.ForbidOff,
                    isActive = (() => zoneData.noPettyJobs),
                    toggleAction = delegate()
                    {
                        zoneData.noPettyJobs = !zoneData.noPettyJobs;
                    }
                };
            }
        }

        //4AM code looks junky, need to refactor this later...
        static bool CheckIfZonesEqual(MapComponent_SmartFarming comp)
        {
            //Get list of zones selected
            var selectedZones = Find.Selector.selected.Where(x => x.GetType() == typeof(Zone_Growing))?.Cast<Zone_Growing>().Select(y => y.ID);
            
            //Go through the IDs
            int i = 0;
            Texture2D lastIcon = null;
            foreach (var zoneID in selectedZones)
            {
                ++i;
                ZoneData tmp;
                if (comp.growZoneRegistry.TryGetValue(zoneID, out tmp))
                {
                    if (i == 1)
                    {
                        lastIcon = tmp.iconCache;
                        continue;
                    }
                    else if (tmp.iconCache != lastIcon)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    //This controls whether or not pawns will skip sow jobs based on the seasonable allowance
    [HarmonyPatch (typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow))]
    static class Patch_GrowthSeasonNow
    {
        static void Postfix(IntVec3 c, Map map, bool forSowing, ref bool __result)
        {
            if (forSowing)
            {
                Zone_Growing zone = map.zoneManager.ZoneAt(c) as Zone_Growing;
                if (zone == null) return;

                var zoneData = compCache[map].growZoneRegistry[zone.ID];

                if (zoneData.sowMode == SowMode.Force || (zone.GetPlantDefToGrow().plant.IsTree && !zone.GetPlantDefToGrow().plant.dieIfLeafless)) __result = true;
                else if (zoneData.sowMode == SowMode.Smart && __result && zoneData.minHarvestDayForNewlySown == -1) __result = false;
            }
        }
    }

    //This replaces the vanilla sow button with our own.
    [HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.GetInspectString))]
    static class Patch_GetInspectString
    {
        static void Postfix(ref string __result, Zone_Growing __instance)
        {
            var zoneData = compCache[__instance.Map].growZoneRegistry[__instance.ID];
            if (zoneData.averageGrowth < __instance.GetPlantDefToGrow().plant.harvestMinGrowth)
            {
                if (zoneData.minHarvestDay > 0) __result += "SmartFarm_MinHarvestDay".Translate() + GenDate.DateFullStringAt(zoneData.minHarvestDay, Find.WorldGrid.LongLatOf(__instance.Map.Tile));
                else __result += "SmartFarm_MinHarvestDayFail".Translate();
            }
            if (zoneData.fertilityAverage != 0) __result += "SmartFarm_Fertility".Translate() + zoneData.fertilityAverage.ToStringPercent();
            if (zoneData.averageGrowth != 0) __result += "SmartFarm_AverageGrowth".Translate() + zoneData.averageGrowth.ToStringPercent();
            if (zoneData.nutritionYield != 0) __result += "SmartFarm_Yield".Translate() + Math.Round(zoneData.nutritionYield, 2);
            if (__instance.GetPlantDefToGrow()?.plant.harvestedThingDef?.ingestible?.HumanEdible ?? false) __result += " (" + Math.Round(zoneData.nutritionYield * processedFoodFactor / compCache[__instance.Map].totalHungerRate, 2) + "SmartFarm_DaysWorth".Translate();
        }
    }

    [HarmonyPatch (typeof(Plant), nameof(Plant.CropBlighted))]
	public static class Patch_CropBlighted
	{
		public static void Postfix(Plant __instance)
		{
			if (autoCutBlighted && __instance.Map.designationManager.DesignationOn(__instance, DesignationDefOf.CutPlant) == null)
			{
				__instance.Map.designationManager.AddDesignation(new Designation(__instance, DesignationDefOf.CutPlant));
			}
		}
	}

    //This is for the "auto cut if dying" mod option
    [HarmonyPatch (typeof(Plant), nameof(Plant.MakeLeafless))]
	public static class Patch_MakeLeafless
	{
		public static void Prefix(Plant __instance)
		{
            if (autoCutDying && __instance.def.plant.dieIfLeafless)
            {
                //This method seem to call before the comps are fully loaded, so we need to check
                var comp = compCache.GetValueSafe(__instance.Map);
                if (comp == null)  return;

                Zone_Growing zone = __instance.Map.zoneManager.ZoneAt(__instance.Position) as Zone_Growing;
                if (zone == null) return;

                var plants = __instance.Map.listerThings.ThingsOfDef(zone.GetPlantDefToGrow())?.Cast<Plant>();
                foreach (var plant in plants)
                {
				    if (plant.Growth >= plant.def.plant.harvestMinGrowth && 
                    !plant.Map.designationManager.HasMapDesignationOn(plant) && 
                    plant.Map.zoneManager.ZoneAt(plant.Position) != null && 
                    !plant.Map.roofGrid.Roofed(plant.Position))
                    {
                        plant.Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                    }
                }
            }
		}
	}
}