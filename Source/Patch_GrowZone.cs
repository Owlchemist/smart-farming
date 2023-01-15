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
                if (AllowHarvest.allowHarvestGizmoPatched) yield return zoneData.allowHarvestGizmo;

                //Harvest now gizmo
                ThingDef crop = __instance.plantDefToGrow;
                if (crop == null) yield break;
                
                var length = __instance.cells.Count;
                for (int i = 0; i < length; i++)
                {
                    var cell = __instance.cells[i];
                    Thing plant = map.thingGrid.ThingAt(cell, ThingCategory.Plant);
                    if (plant?.def == crop && crop.plant.harvestedThingDef != null && ((Plant)plant).Growth >= crop.plant.harvestMinGrowth)
                    {
                        yield return zoneData.harvestGizmo;
                        break;
                    }
                }

                //Orchard align?
                if (orchardAlignment && crop.plant.blockAdjacentSow) yield return zoneData.orchardGizmo;
            }
        }

        static IEnumerable<Gizmo> GetMultiZoneGizmos(MapComponent_SmartFarming comp, ZoneData zoneData, Zone_Growing thisZone)
        {
            ZoneData basisZoneData = zoneData;
            Zone_Growing basisZone = null;
            foreach (var zone in Find.Selector.selected)
            {
                if (zone is Zone_Growing growZone && comp.growZoneRegistry.TryGetValue(growZone.ID, out basisZoneData))
                {
                    basisZone = growZone;
                    break;
                }
            }
            
            yield return new Command_Action()
            {
                defaultLabel = ("SmartFarming.Icon.SetAll".Translate() + basisZoneData.sowGizmo.defaultLabel.ToLower()),
                defaultDesc = basisZoneData.sowGizmo.defaultDesc,
                hotKey = KeyBindingDefOf.Command_ItemForbid,
                icon = basisZoneData.iconCache[basisZoneData.sowMode],
                action = () => zoneData.SwitchSowMode(comp, thisZone, basisZoneData.sowMode)
            };
            yield return new Command_Action()
            {
                defaultLabel = ("SmartFarming.Icon.SetAll".Translate() + basisZoneData.priorityGizmo.defaultLabel.ToLower()),
                defaultDesc = basisZoneData.priorityGizmo.defaultDesc,
                icon = ResourceBank.iconPriority,
                action = () => zoneData.SwitchPriority(basisZoneData.priority)
            };
            if (basisZone != null)
            {
                yield return new Command_Action()
                {
                    defaultLabel = "SmartFarming.Icon.MergeZones".Translate(),
                    defaultDesc = "SmartFarming.Icon.MergeZones.Desc".Translate(),
                    icon = ResourceBank.mergeZones,
                    action = () => zoneData.MergeZones(thisZone, basisZone)
                };
            }
            yield break;
        }
    }

    //This controls whether or not pawns will skip sow jobs based on the seasonable allowance
    [HarmonyPatch (typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow))]
    [HarmonyPriority(HarmonyLib.Priority.Last)]
    static class Patch_GrowthSeasonNow
    {
        static bool Prefix(ref bool __result, Map map, IntVec3 c, bool forSowing)
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
        static float totalHungerRate = 0f;
        static string Postfix(string __result, Zone_Growing __instance)
        {
            Map map = __instance.Map;
            if (compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming mapComp) && mapComp.growZoneRegistry.TryGetValue(__instance.ID, out ZoneData zoneData))
            {
                //Update the hunger cache only when it's being viewed
                if (totalHungerRate == 0f || Find.TickManager.TicksGame % 480 == 0) {
                    try
                    {
                        totalHungerRate = mapComp.CalculateTotalHungerRate();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Smart Farming] Error calculating hunger rate" + ex);
                        totalHungerRate = 1f;
                    }
                }

                StringBuilder builder = new StringBuilder(__result, 10);
                if (zoneData.averageGrowth < __instance.plantDefToGrow?.plant.harvestMinGrowth)
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
	static class AutoCutIfBlighted
	{
		static void Postfix(Plant __instance)
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
	static class AutoCutIfDying
	{
        static bool Prepare()
        {
            return autoCutDying;
        }

		static void Prefix(Plant __instance)
		{
            if (__instance.def.plant.dieIfLeafless)
            {
                Map map = __instance.Map;
                compCache.TryGetValue(map.uniqueID)?.HarvestNow(map.zoneManager.zoneGrid[__instance.positionInt.z * map.info.sizeInt.x + __instance.positionInt.x] as Zone_Growing);
            }
		}
	}

    //This is for the "allow harvest" gizmo
    [HarmonyPatch(typeof(WorkGiver_GrowerHarvest), nameof(WorkGiver_GrowerHarvest.HasJobOnCell))]
	static class AllowHarvest
	{
        public static bool allowHarvestGizmoPatched = false;
        static bool Prepare()
        {
            allowHarvestGizmoPatched = allowHarvestOption;
            return allowHarvestOption;
        }
		static bool Prefix(Pawn pawn, IntVec3 c)
		{
            Map map = pawn?.Map;

            //We don't check the zone type because it's faster for the collection lookup to return with nothing than it is to cast the zone
            int zoneID = map?.zoneManager.zoneGrid[c.z * map.info.sizeInt.x + c.x]?.ID ?? -1;
            if (zoneID == -1) return true;

            if (compCache.TryGetValue(map.uniqueID, out MapComponent_SmartFarming mapComp) && mapComp.growZoneRegistry.TryGetValue(zoneID, out ZoneData zoneData))
            {
                return zoneData.allowHarvest;
            }

            return true;
		}
	}

    //Skip the contigious check for merged zones
    [HarmonyPatch(typeof(Zone), nameof(Zone.CheckContiguous))]
	static class Patch_CheckContiguous
	{
        static bool Prefix(Zone __instance)
        {
			return !(__instance is Zone_Growing zone && 
            compCache.TryGetValue(zone.zoneManager.map.uniqueID, out MapComponent_SmartFarming mapComp) && 
            mapComp.growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData) && zoneData.isMerged);       
        }
    }
}