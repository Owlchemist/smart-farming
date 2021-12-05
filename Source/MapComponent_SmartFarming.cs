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
		int ticks, currentDay, tile, hour, sunrise, sunset;
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

			//Add placeholder registy if missing
			if (growZoneRegistry == null) growZoneRegistry = new Dictionary<int, ZoneData>();

			//Find any missing zones (for when the mod is installed for an existing save)
			foreach (Zone zone in map.zoneManager.AllZones)
			{
				Zone_Growing growZone = zone as Zone_Growing;
				if (growZone != null && !growZoneRegistry.ContainsKey(growZone.ID))
				{
					growZoneRegistry.Add(growZone.ID, new ZoneData());
					CalculateAll(growZone);
				}
			}

			//Validate data
			var allValidZones = map.zoneManager.AllZones.Where(x => x.GetType() == typeof(Zone_Growing)).Select(y => y.ID);
			foreach (int zoneID in growZoneRegistry.Keys.ToList())
			{
				if (!allValidZones.Contains(zoneID))
				{
					if (Prefs.DevMode) Log.Message("[Smart Farming] Removing invalid key # " + zoneID);
					growZoneRegistry.Remove(zoneID);
				}
			}

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
		}

		public void SwitchPriority(Zone_Growing zone)
		{
			SoundDefOf.Click.PlayOneShotOnCamera(null);
			
			ZoneData zoneData = growZoneRegistry[zone.ID];
			int length = Enum.GetValues(typeof(Priority)).Length;

			zoneData.priority = zoneData.priority != Priority.Critical ? ++zoneData.priority : Priority.Low;
		}
		public void SwitchSowMode(Zone_Growing zone)
		{
			SoundDefOf.Click.PlayOneShotOnCamera(null);
			ZoneData zoneData = growZoneRegistry[zone.ID];
			zoneData.basicMode = false;
			switch (zoneData.sowMode)
			{
				case SowMode.Force:
					zoneData.sowMode = SowMode.Off;
					zoneData.iconCache = sowIconOff;
					zone.allowSow = false;
					break;
				case SowMode.On:
					zoneData.sowMode = SowMode.Smart;
					zoneData.iconCache = sowIconSmart;
					CalculateAll(zone);
					zone.allowSow = true;
					break;
				case SowMode.Smart:
					zoneData.sowMode = SowMode.Force;
					zoneData.iconCache = sowIconForce;
					zone.allowSow = true;
					break;
				default:
					zoneData.sowMode = SowMode.On;
					zoneData.iconCache = sowIconOn;
					zone.allowSow = true;
					break;
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
			if (zoneData.noPettyJobs && zoneData.sowMode != SowMode.Off)
			{
				float validPlants = numOfPlants - newPlants;
				zone.allowSow = !(validPlants > 0 && 1 - (validPlants / (float)numOfCells) < pettyJobs);
			}
		}

		private long CalculateDaysToHarvest(Zone_Growing zone, ZoneData zoneData, bool forSowing = false)
		{
			report.Clear();

			//Check for toxic fallout first
			if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout) && !map.roofGrid.Roofed(zone.Position)){
				return -1;
			}

			ThingDef plant = zone.GetPlantDefToGrow();
			if (plant == null) return -1;

			//Prepare variables
			
			int growthNeeded = (int)(plant.plant.growDays * plant.plant.harvestMinGrowth * 60000f * 1.1f * (1f - (forSowing ? 0f : zoneData.averageGrowth / plant.plant.harvestMinGrowth)));
			int simulatedGrowth = 0;
			int numOfDays = 0;
			int lifespan = (int)plant.plant.LifespanDays;
			
			while (simulatedGrowth < growthNeeded && simulatedGrowth != -1)
			{
				simulatedGrowth = SimulateDay(numOfDays, simulatedGrowth, zone, plant, zoneData, world, tile);

				if (++numOfDays > 360)
				{
					Log.Warning("[Smart Farming] failed simulating " + plant.defName + " at zone " + zone.Position);
					simulatedGrowth = -1;
				}
				else if (lifespan < numOfDays) simulatedGrowth = -1;
			}

			if (logging && Prefs.DevMode) Log.Message("[Smart Farming] simulation report: \n" + string.Join("\n", report));

			return simulatedGrowth == -1 ? -1 : (numOfDays * 60000) + Find.TickManager.TicksAbs;
		}

		int SimulateDay(int numOfDays, int simulatedGrowth, Zone_Growing zone, ThingDef plant, ZoneData zoneData, RimWorld.Planet.World world, int tile)
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
			numOfDays += currentDay;

			//Fertility
			float fertilityFactor = PlantUtility.GrowthRateFactorFor_Fertility(plant, useAverageFertility ? zoneData.fertilityAverage : zoneData.fertilityLow);
			int growthToday =  (int)(ticksOfLight * fertilityFactor);

			//Temperature
			int numOfDayTicks = (int)(numOfDays * 60000);
			float low = baseTemperature + OffsetFromSeasonCycle((int)(numOfDays * 60000) + sunrise) + sunLow + tempOffsetCache;
			float high = baseTemperature + OffsetFromSeasonCycle((int)(numOfDays * 60000) + sunset) + sunHigh + tempOffsetCache;
			float average = (low + high) / 2f;
			growthToday = (int)(growthToday * PlantUtility.GrowthRateFactorFor_Temperature(average));
			
			//Results, use -1 if the plan will die/never grow
			simulatedGrowth = (fertilityFactor == 0 || (plant.plant.dieIfLeafless && Math.Min(low, high) < minTempAllowed)) ? -1 : simulatedGrowth + growthToday;

			//Special check for freezing ground
			if (!coldSowing && numOfDays - currentDay == 0 && low <= 0) simulatedGrowth = -1;

			//Debug
			if (logging && Prefs.DevMode)
			{
				report.Add(" - day: " + GenDate.DayOfYear(numOfDayTicks, latitude) + 
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
				totalHungerRate += Need_Food.BaseHungerRateFactor(pawn.ageTracker.CurLifeStage, pawn.def) * pawn.health.hediffSet.HungerRateFactor * 
				((pawn.story == null || pawn.story.traits == null) ? 1f : pawn.story.traits.HungerRateFactor) * pawn.GetStatValue(StatDefOf.HungerRateMultiplier, true);
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

			foreach (Zone zone in map.zoneManager.AllZones)
			{
				Zone_Growing growZone = zone as Zone_Growing;
				if (growZone != null) CalculateAll(growZone, false);
			}
		}

		public void CalculateAll(Zone_Growing zone, bool cacheNow = true, bool resetMode = false)
		{
			if (growZoneRegistry.TryGetValue(zone.ID, out ZoneData zoneData))
			{
				if (resetMode) zoneData.basicMode = false;
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

		public void HarvestNow(Zone_Growing zone)
		{
			ThingDef crop = zone.GetPlantDefToGrow();
			foreach (IntVec3 cell in zone.cells)
			{
				Plant plant = zone.Map?.thingGrid.ThingAt<Plant>(cell);
				if (plant?.def == crop && plant.Growth >= crop.plant.harvestMinGrowth) plant.Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
			}
		}
	}
}