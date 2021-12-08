using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System;
using static SmartFarming.Mod_SmartFarming;
using static SmartFarming.ModSettings_SmartFarming;
using static SmartFarming.ZoneData;

namespace SmartFarming
{
    //This handles the zone gizmos
    [HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos))]
    static class Patch_GetGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Zone_Growing __instance)
        {
            //Pass along all other gizmos except vanilla sow, which we only identify via its hotkey...
            foreach (var value in values)
            {
                if (((Command)value)?.hotKey == KeyBindingDefOf.Command_ItemForbid) continue;
                yield return value;
            }

            Map map = __instance.Map;
            if (compCache.TryGetValue(map, out MapComponent_SmartFarming comp) && comp.growZoneRegistry.TryGetValue(__instance.ID, out ZoneData zoneData))
            {
                //Return the sow mode gizmo and priority gizmo
                if (Find.Selector.selected.Count == 1)
                {
                    yield return zoneData.sowGizmo;
                    yield return zoneData.priorityGizmo;
                }
                else
                {
                    foreach (var gizmo in GetMultiZoneGizmos(comp, zoneData, __instance))
                    {
                        yield return gizmo;
                    }
                }
                
                //Petty jobs gizmo
                yield return zoneData.pettyJobsGizmo;

                //Harvest now gizmo
                ThingDef crop = __instance.plantDefToGrow;
                foreach (IntVec3 cell in __instance.cells)
                {
                    Plant plant = map.thingGrid.ThingAt<Plant>(cell);
                    if (plant?.def == crop && crop.plant.harvestedThingDef != null && plant.Growth >= crop.plant.harvestMinGrowth)
                    {
                        yield return zoneData.harvestGizmo;
                        break;
                    }
                }
            }
        }

        static IEnumerable<Gizmo> GetMultiZoneGizmos(MapComponent_SmartFarming comp, ZoneData zoneData, Zone_Growing thizZone)
        {
            ZoneData basisZone = zoneData;
            foreach (var zone in Find.Selector.selected)
            {
                Zone_Growing growZone = zone as Zone_Growing;
                if (growZone != null && comp.growZoneRegistry.TryGetValue(growZone.ID, out ZoneData tmp))
                {
                    basisZone = tmp;
                    break;
                }
            }
            
            yield return new Command_Action()
            {
                defaultLabel = ("SmartFarming.Icon.SetAll".Translate() + basisZone.sowGizmo.defaultLabel.ToLower()),
                defaultDesc = basisZone.sowGizmo.defaultDesc,
                hotKey = KeyBindingDefOf.Command_ItemForbid,
                icon = basisZone.iconCache[basisZone.sowMode],
                action = () => zoneData.SwitchSowMode(comp, thizZone, basisZone.sowMode)
            };
            yield return new Command_Action()
            {
                defaultLabel = ("SmartFarming.Icon.SetAll".Translate() + basisZone.priorityGizmo.defaultLabel.ToLower()),
                defaultDesc = basisZone.priorityGizmo.defaultDesc,
                icon = ResourceBank.iconPriority,
                action = () => zoneData.SwitchPriority(basisZone.priority)
            };
            yield break;
        }
    }

    //This controls whether or not pawns will skip sow jobs based on the seasonable allowance
    [HarmonyPatch (typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow))]
    static class Patch_GrowthSeasonNow
    {
        static void Postfix(Map map, ref bool __result, IntVec3 c, bool forSowing)
        {
            if (forSowing)
            {
                Zone_Growing zone = map.zoneManager.ZoneAt(c) as Zone_Growing;
                if (zone == null) return;

                var zoneData = compCache.TryGetValue(map)?.growZoneRegistry[zone.ID];
                if (zoneData == null) return;

                if (zoneData.sowMode == SowMode.Force || (coldSowing && zone.GetPlantDefToGrow().plant.IsTree && !zone.GetPlantDefToGrow().plant.dieIfLeafless)) __result = true;
                else if (zoneData.sowMode == SowMode.Smart) __result = zoneData.minHarvestDayForNewlySown > -1;
            }
        }
    }

    //This adds information to the inspector window
    [HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.GetInspectString))]
    static class Patch_GetInspectString
    {
        static void Postfix(ref string __result, Zone_Growing __instance)
        {
            var zoneData = compCache[__instance.Map].growZoneRegistry[__instance.ID];
            if (zoneData.averageGrowth < __instance.GetPlantDefToGrow().plant.harvestMinGrowth)
            {
                if (zoneData.minHarvestDay > 0) __result += "SmartFarming.Inspector.MinHarvestDay".Translate() + GenDate.DateFullStringAt(zoneData.minHarvestDay, Find.WorldGrid.LongLatOf(__instance.Map.Tile));
                else __result += "SmartFarming.Inspector.MinHarvestDayFail".Translate();
            }
            if (zoneData.fertilityAverage != 0) __result += "SmartFarming.Inspector.Fertility".Translate() + zoneData.fertilityAverage.ToStringPercent();
            if (zoneData.averageGrowth != 0) __result += "SmartFarming.Inspector.AverageGrowth".Translate() + zoneData.averageGrowth.ToStringPercent();
            if (zoneData.nutritionYield != 0) __result += "SmartFarming.Inspector.Yield".Translate() + Math.Round(zoneData.nutritionYield, 2);
            if (__instance.GetPlantDefToGrow()?.plant.harvestedThingDef?.ingestible?.HumanEdible ?? false) __result += " (" + Math.Round(zoneData.nutritionYield * processedFoodFactor / compCache[__instance.Map].totalHungerRate, 2) + "SmartFarming.Inspector.DaysWorth".Translate();
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
                MapComponent_SmartFarming comp = compCache.GetValueSafe(__instance.Map);
                if (comp == null) return;

                //Is this plant part of a zone?
                Zone_Growing zone = __instance.Map.zoneManager.ZoneAt(__instance.Position) as Zone_Growing;
                if (zone == null) return;

                __instance.Map.listerThings.ThingsOfDef(zone.GetPlantDefToGrow()).ForEach
                (x =>
                    {
                        Plant plant = x as Plant;
                        if (plant?.Growth >= plant.def.plant.harvestMinGrowth && //Ready for harvest?
                        plant.def.plant.harvestedThingDef != null && //Can be harvested?
                        !plant.Map.designationManager.HasMapDesignationOn(plant) &&  //Is not already designatd?
                        plant.Map.zoneManager.ZoneAt(plant.Position) != null && //Is part of a zone?
                        !plant.Map.roofGrid.Roofed(plant.Position)) //Is not roofed?
                        {
                            plant.Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                        }
                    }
                );
            }
		}
	}

}