using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System;
using System.Text;
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
            if (compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming comp) && comp.growZoneRegistry.TryGetValue(__instance.ID, out ZoneData zoneData))
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
                //Allow harvest gizmo
                if (allowHarvestOption) yield return zoneData.allowHarvestGizmo;

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
        static bool Prefix(Map map, ref bool __result, IntVec3 c, bool forSowing)
        {
            if (forSowing)
            {
                Zone_Growing zone = map.zoneManager.zoneGrid[c.z * map.info.sizeInt.x + c.x] as Zone_Growing;

                if (zone != null && compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming comp) && comp.growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData))
                {
                    switch (zoneData.sowMode)
                    {
                        case SowMode.Smart:
                        {
                            __result = zoneData.alwaysSow? true : zoneData.minHarvestDayForNewlySown > -1;
                            return false;
                        }
                        case SowMode.Force:
                        {
                            __result = true;
                            return false;
                        }
                        case SowMode.On:
                        {
                            return true; //Vanilla handling
                        }
                        case SowMode.Off:
                        {
                            __result = false;
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }

    //This adds information to the inspector window
    [HarmonyPatch (typeof(Zone_Growing), nameof(Zone_Growing.GetInspectString))]
    static class Patch_GetInspectString
    {
        static float totalHungerRate = -1f;
        static string Postfix(string __result, Zone_Growing __instance)
        {
            Map map = __instance.Map;
            if (compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming mapComp) && mapComp.growZoneRegistry.TryGetValue(__instance.ID, out ZoneData zoneData))
            {
                //Update the hunger cache only when it's being viewed
                if (totalHungerRate == -1f || Find.TickManager.TicksGame % 480 == 0) totalHungerRate = mapComp.CalculateTotalHungerRate();

                StringBuilder builder = new StringBuilder(__result, 10);
                if (zoneData.averageGrowth < __instance.plantDefToGrow.plant.harvestMinGrowth)
                {
                    if (zoneData.minHarvestDay > 0){
                        builder.Append(ResourceBank.minHarvestDay);
                        builder.Append(GenDate.DateFullStringAt(zoneData.minHarvestDay, Find.WorldGrid.LongLatOf(map.Tile)));
                    }
                    else
                        builder.Append(ResourceBank.minHarvestDayFail);
                }
                if (zoneData.fertilityAverage != 0)
                    builder.Append("SmartFarming.Inspector.Fertility".Translate(zoneData.fertilityAverage.ToStringPercent(), zoneData.fertilityLow.ToStringPercent()));
                if (zoneData.nutritionYield != 0){
                    builder.Append(ResourceBank.yield);
                    builder.Append(Math.Round(zoneData.nutritionYield, 2));
                }
                if (__instance.plantDefToGrow?.plant.harvestedThingDef?.ingestible?.HumanEdible ?? false)
                    builder.Append("SmartFarming.Inspector.DaysWorth".Translate(Math.Round(zoneData.nutritionYield * processedFoodFactor / totalHungerRate, 2)));

                return builder.ToString();
            }
            else return __result;
        }
    }

    //This is for the "auto cut blighted" mod option
    [HarmonyPatch (typeof(Plant), nameof(Plant.CropBlighted))]
	public static class Patch_CropBlighted
	{
		public static void Postfix(Plant __instance)
		{
			Map map = __instance.Map;
            if (autoCutBlighted && map.designationManager.DesignationOn(__instance, DesignationDefOf.CutPlant) == null)
			{
				map.designationManager.AddDesignation(new Designation(__instance, DesignationDefOf.CutPlant));
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
                Map map = __instance.Map;
                compCache.TryGetValue(map.uniqueID)?.HarvestNow(map.zoneManager.zoneGrid[__instance.positionInt.z * map.info.sizeInt.x + __instance.positionInt.x] as Zone_Growing);
            }
		}
	}

    //This is for the "allow harvest" gizmo
    [HarmonyPatch(typeof(WorkGiver_GrowerHarvest), nameof(WorkGiver_GrowerHarvest.HasJobOnCell))]
	internal static class Patch_WorkGiver_GrowerHarvest
	{
		private static bool Prefix(Pawn pawn, IntVec3 c)
		{
            if (!allowHarvestOption) return true;
            Map map = pawn?.Map;

            Zone_Growing zone = map?.zoneManager.zoneGrid[c.z * map.info.sizeInt.x + c.x] as Zone_Growing;
            if (zone == null) return true;

            var zoneData = compCache?.TryGetValue(map.uniqueID)?.growZoneRegistry?.TryGetValue(zone.ID);
            if (zoneData == null) return true;

            return zoneData.allowHarvest;
		}
	}
}