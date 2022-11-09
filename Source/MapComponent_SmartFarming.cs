using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse.Sound;
using System;
using static SmartFarming.Mod_SmartFarming;
using static SmartFarming.ZoneData;
using static SmartFarming.ModSettings_SmartFarming;
using static SmartFarming.ResourceBank;
 
namespace SmartFarming
{
    public class MapComponent_SmartFarming : MapComponent
	{
		int ticks, currentDay, tile, hour, sunrise, sunset, lastMessageDay = -1;
		public Dictionary<int, ZoneData> growZoneRegistry = new Dictionary<int, ZoneData>();
		public float totalHungerRate, tempOffsetCache, latitude, longitudeTuning, baseTemperature, worldAverage, sunLow, sunHigh;
		List<string> report = new List<string>();
		RimWorld.Planet.World world;

		public MapComponent_SmartFarming(Map map) : base(map){}

		public override void ExposeData()
		{
			Scribe_Collections.Look(ref this.growZoneRegistry, "growZoneRegistry");
		}

		public override void FinalizeInit()
		{
			//Quick cache
			compCache.Add(map, this);
			CalculateTotalHungerRate();

			//Cache some frequently used getters that don't change
			latitude = Find.WorldGrid.LongLatOf(map.Tile).x;
			world = Find.World;
			tile = map.Tile;
			worldAverage = Season.Winter.GetMiddleTwelfth(0f).GetBeginningYearPct();
			float latitudeAb = System.Math.Abs(latitude);
			int tmp = (int)(30000f * (latitudeAb > 90f ? 90f / latitudeAb : latitudeAb / 90f));
			sunrise = 15000 + tmp;
			sunset = 47500 + tmp;

			//Cache longitude tuning
			if (world.grid.LongLatOf(map.Tile).y >= 0f) longitudeTuning = TemperatureTuning.SeasonalTempVariationCurve.Evaluate(world.grid.DistanceFromEquatorNormalized(tile));
			else longitudeTuning = -TemperatureTuning.SeasonalTempVariationCurve.Evaluate(world.grid.DistanceFromEquatorNormalized(tile));

			//Add placeholder registy if missing
			if (growZoneRegistry == null) growZoneRegistry = new Dictionary<int, ZoneData>();

			//Find any missing zones (for when the mod is installed for an existing save)
			foreach (Zone zone in map.zoneManager.AllZones)
			{
				Zone_Growing growZone = zone as Zone_Growing;
				if (growZone != null && !growZoneRegistry.ContainsKey(growZone.ID))
				{
					growZoneRegistry.Add(growZone.ID, new ZoneData());
					growZoneRegistry[growZone.ID].Init(this, growZone);
					CalculateAll(growZone);
				}
			}

			//Validate data
			var allValidZones = map.zoneManager.AllZones.Where(x => x is Zone_Growing).Select(y => y.ID);
			foreach (var zoneData in growZoneRegistry.ToList())
			{
				int zoneID = zoneData.Key;
				if (!allValidZones.Contains(zoneID))
				{
					if (Prefs.DevMode) Log.Message("[Smart Farming] Removing invalid key # " + zoneID);
					growZoneRegistry.Remove(zoneID);
				}
				else
				{
					zoneData.Value.Init(this, map.zoneManager.AllZones.FirstOrDefault(x => x.ID == zoneID) as Zone_Growing);
				}
			}
		}
		
		private void CalculateAverages(Zone_Growing zone, ZoneData zoneData)
		{
			int numOfCells = zone.cells.Count;
			int numOfPlants = 0;
			int newPlants = 0;
			float fertility = 0f;
			float lowestFertility = 99f;
			float growth = 0f;

			for (int n = 0; n < numOfCells; ++n)
			{
				//Fertility calculations
				IntVec3 index = zone.cells[n];
				float fertilityHere = map.fertilityGrid.FertilityAt(index);
				fertility += fertilityHere;
				if (fertilityHere < lowestFertility) lowestFertility = fertilityHere;

				//Plant tally
				Plant plant = map.thingGrid.ThingAt<Plant>(index);
				if (plant != null && plant.def == zone.plantDefToGrow)
				{
					growth += plant.growthInt;
					++numOfPlants;
					if (plant.growthInt < 0.08f) ++newPlants;
				}
			}

			//Save fertility data
			zoneData.fertilityAverage = fertility / numOfCells;
			zoneData.fertilityLow = lowestFertility;

			//Save growth data
			zoneData.averageGrowth = (numOfPlants > 0) ? growth / numOfPlants : 0f;

			//Process petty jobs since we already have all the data needed
			if (zoneData.noPettyJobs)
			{
				if (zoneData.sowMode != SowMode.Off)
				{
					float validPlants = numOfPlants - newPlants;
					zone.allowSow = !(validPlants > 0 && 1 - (validPlants / (float)numOfCells) < pettyJobs);
				}
			}
			//If not using petty jobs, validate the sowmode
			else zone.allowSow = zoneData.sowMode != SowMode.Off;
		}

		private long CalculateDaysToHarvest(Zone_Growing zone, ZoneData zoneData, bool forSowing = false)
		{
			//Check for toxic fallout first
			if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout) && !map.roofGrid.Roofed(zone.Position)){
				return -1;
			}

			ThingDef plant = zone.GetPlantDefToGrow();
			if (plant == null) return -1;

			//Prepare variables
			List<string> simulationReport = new List<string>();
			int growthNeeded = (int)(plant.plant.growDays * plant.plant.harvestMinGrowth * 60000f * 1.1f * (1f - (forSowing ? 0f : zoneData.averageGrowth / plant.plant.harvestMinGrowth)));
			int simulatedGrowth = 0;
			int numOfDays = 0;
			int lifespan = (int)plant.plant.LifespanDays;
			
			while (simulatedGrowth < growthNeeded && simulatedGrowth != -1)
			{
				simulatedGrowth = SimulateDay(numOfDays, simulatedGrowth, zone, plant, zoneData, world, tile, simulationReport);

				if (++numOfDays > 360)
				{
					Log.Warning("[Smart Farming] failed simulating " + plant.defName + " at zone " + zone.Position);
					simulatedGrowth = -1;
				}
				else if (lifespan < numOfDays) simulatedGrowth = -1;
			}

			if (logging && Prefs.DevMode)
			{
				string reportPrint = simulationReport.Count > 0 ? ("\n" + string.Join("\n", simulationReport)) : "skipped";
				report.Add(" - " + (forSowing ? "new sowing ": "") + "report for " + 
					zone.Position.ToString()  + " (" + zone.plantDefToGrow.defName + ") : " + reportPrint);
			} 

			return simulatedGrowth == -1 ? -1 : (numOfDays * 60000) + Find.TickManager.TicksAbs;
		}

		int SimulateDay(int numOfDays, int simulatedGrowth, Zone_Growing zone, ThingDef plant, ZoneData zoneData, RimWorld.Planet.World world, int tile, List<string> simulationReport)
		{
			int ticksOfLight = 32500; // 32500 = 60,000 ticks * .54167, only the hours this plant is "awake"
			
			//This adjusts the ticks of light if we're doing a partial day calculation, depending on what hour it currently is
			if (numOfDays == 0)
			{
				hour = 14 - hour;
				if (hour < 1)
				{
					if (hour < -3) hour = 13; //Past midnight, consider it a full day
					else hour = 0;
				}
				
				ticksOfLight = 2500 * hour;
			}

			//Prepare date
			int dayToSimulate = numOfDays + currentDay;

			//Fertility
			float fertilityFactor = PlantUtility.GrowthRateFactorFor_Fertility(plant, useAverageFertility ? zoneData.fertilityAverage : zoneData.fertilityLow);
			int growthToday =  (int)(ticksOfLight * fertilityFactor);

			//Temperature
			int numOfDayTicks = (int)(dayToSimulate * 60000);
			float low = baseTemperature + OffsetFromSeasonCycle((int)(dayToSimulate * 60000) + sunrise) + sunLow + tempOffsetCache;
			float high = baseTemperature + OffsetFromSeasonCycle((int)(dayToSimulate * 60000) + sunset) + sunHigh + tempOffsetCache;
			float average = (low + high) / 2f;
			growthToday = (int)(growthToday * PlantUtility.GrowthRateFactorFor_Temperature(average));
			
			//Results, use -1 if the plan will die/never grow
			simulatedGrowth = (fertilityFactor == 0 || (plant.plant.dieIfLeafless && Math.Min(low, high) < minTempAllowed)) ? -1 : simulatedGrowth + growthToday;

			//Special check for freezing ground
			if (!coldSowing && numOfDays == 0 && low <= 0) simulatedGrowth = -1;

			//Dangerously cold tonight? Mark plants for harvest nd Give a message and avoid message spam
			if (autoHarvestNow && numOfDays < 2 && simulatedGrowth == -1 && HarvestNow(zone) > 0 && currentDay != lastMessageDay) 
			{
				lastMessageDay = currentDay;
				Messages.Message("SmartFarming.Message.AutoHarvestNow".Translate(), MessageTypeDefOf.NeutralEvent, false);	
			}

			//Debug
			if (logging && Prefs.DevMode)
			{
				simulationReport.Add("   - day: " + dayToSimulate + 
					" | temperature: " + Math.Round(low, 2) + " to " + Math.Round(high, 2) +  
					" | growth: " + simulatedGrowth.ToString() + 
					" | fertility: " + fertilityFactor.ToStringPercent() + 
					" | temperature: " + PlantUtility.GrowthRateFactorFor_Temperature(average).ToStringPercent());
			}

			return simulatedGrowth;
		}

		float OffsetFromSeasonCycle(int absTick)
		{
			float num = (float)(absTick / 60000 % 60) / 60f;
			return (float)System.Math.Cos(6.2831855f * (num - worldAverage)) * -longitudeTuning;
		}

		void CalculateYield(Zone_Growing zone, ZoneData zoneData)
		{
			//Reset
			zoneData.nutritionYield = 0f;

			if (zone.plantDefToGrow == null) return;

			//Fetch plant's produce
			if (zone.plantDefToGrow.plant.harvestedThingDef == null) return;

			//Calculate the yield
			float num = zone.plantDefToGrow.plant.harvestYield * Current.gameInt.storyteller.difficulty.cropYieldFactor * zone.cells.Count;
			if (zoneData.nutritionCache == 0) zoneData.nutritionCache = zone.plantDefToGrow?.plant?.harvestedThingDef?.GetStatValueAbstract(StatDefOf.Nutrition, null) ?? 0;
			zoneData.nutritionYield = zoneData.nutritionCache * num;
		}

		public void CalculateTotalHungerRate()
		{
			totalHungerRate = 0; //Reset
			foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
			{
				totalHungerRate += (Need_Food.BaseHungerRate(pawn.ageTracker.CurLifeStage, pawn.def) * 60000f) * HungerCategory.Fed.HungerMultiplier() * pawn.health.hediffSet.GetHungerRateFactor(true ? null : HediffDefOf.Malnutrition) * 
				(pawn.story?.traits?.HungerRateFactor ?? 1f);
			}
		}

		public override void MapComponentTick()
		{
			if (++ticks == 2500) //Hourly
			{
				ticks = 0;
				ProcessZones();
			}
		}

		public void ProcessZones(bool cacheNow = false)
		{
			UpdateCommonCache();
			CalculateTotalHungerRate();

			report.Clear();
			foreach (Zone zone in map.zoneManager.AllZones)
			{
				Zone_Growing growZone = zone as Zone_Growing;
				if (growZone != null) CalculateAll(growZone, false);
			}
			if (logging && Prefs.DevMode) Log.Message("[Smart Farming] Simulation report: \n" + string.Join("\n", report));
		}

		public void CalculateAll(Zone_Growing zone, bool cacheNow = true)
		{
			if (growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData))
			{
				if (cacheNow) UpdateCommonCache();

				CalculateAverages(zone, zoneData);
				zoneData.minHarvestDay = CalculateDaysToHarvest(zone, zoneData, false);
				zoneData.minHarvestDayForNewlySown = CalculateDaysToHarvest(zone, zoneData, true);
				CalculateYield(zone, zoneData);
			}
		}

		void UpdateCommonCache()
		{
			tempOffsetCache = map.gameConditionManager.AggregateTemperatureOffset();
			currentDay = GenDate.DayOfYear(Current.gameInt.tickManager.TicksAbs, latitude);
			baseTemperature = world.grid[tile].temperature;
			sunLow = (float)System.Math.Cos(6.2831855f * (GenDate.DayPercent((long)(currentDay * 60000) + sunrise, latitude) + 0.32f)) * 7f;
			sunHigh = (float)System.Math.Cos(6.2831855f * (GenDate.DayPercent((long)(currentDay * 60000) + sunset, latitude) + 0.32f)) * 7f;
			hour = GenDate.HourOfDay(Current.gameInt.tickManager.ticksGameInt, latitude);
		}

		public int HarvestNow(Zone_Growing zone, bool roofCheck = true)
		{
			ThingDef crop = zone?.plantDefToGrow;
			if (crop == null) return 0;
			Map map = zone.Map;

			int result = 0;
			int length = zone.cells.Count;
			for (int i = 0; i < length; ++i)
			{
				Plant plant = map.thingGrid.ThingAt<Plant>(zone.cells[i]) as Plant;

				if (plant?.def == crop && //Is this the right plant?
				plant.Growth >= plant.def.plant.harvestMinGrowth && //Ready for harvest?
				plant.def.plant.harvestedThingDef != null && //Can be harvested?
				plant.def.plant.dieIfLeafless && //Can die to the cold?
				!map.designationManager.HasMapDesignationOn(plant) &&  //Is not already designatd?
				(!roofCheck || !map.roofGrid.Roofed(plant.Position))) //Is not roofed?
				{
					++result;
					map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
				}
			}
			return result;
		}
	}
}