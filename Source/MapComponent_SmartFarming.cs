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
			map.zoneManager.AllZones.ForEach(x => {
				if (x.GetType() == typeof(Zone_Growing) && !growZoneRegistry.ContainsKey(x.ID))
				{
					growZoneRegistry.Add(x.ID,new ZoneData());
					CalculateAll((Zone_Growing)x);
				}
			});

			//Validate data
			var allValidZones = map.zoneManager.AllZones.Where(x => x.GetType() == typeof(Zone_Growing)).Select(y => y.ID);
			var workingList = growZoneRegistry.ToList();
			foreach (var registration in workingList)
			{
				if (!allValidZones.Contains(registration.Key))
				{
					if (Prefs.DevMode) Log.Message("[Smart Farming] Removing invalid key # " + registration.Key);
					growZoneRegistry.Remove(registration.Key);
				}
			}
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
			switch (growZoneRegistry[zone.ID].sowMode)
			{
				case SowMode.Force:
					growZoneRegistry[zone.ID].sowMode = SowMode.Off;
					growZoneRegistry[zone.ID].iconCache = sowIconOff;
					zone.allowSow = false;
					break;
				case SowMode.On:
					growZoneRegistry[zone.ID].sowMode = SowMode.Smart;
					growZoneRegistry[zone.ID].iconCache = sowIconSmart;
					CalculateAll(zone);
					zone.allowSow = true;
					break;
				case SowMode.Smart:
					growZoneRegistry[zone.ID].sowMode = SowMode.Force;
					growZoneRegistry[zone.ID].iconCache = sowIconForce;
					zone.allowSow = true;
					break;
				default:
					growZoneRegistry[zone.ID].sowMode = SowMode.On;
					growZoneRegistry[zone.ID].iconCache = sowIconOn;
					zone.allowSow = true;
					break;
			}
		}
		
		private void CalculateAverages(Zone_Growing zone, ZoneData zoneData)
		{
			List<IntVec3> cells = zone.Cells;
			int numOfCells = zone.cells.Count;
			int numOfPlants = 0;
			int newPlants = 0;
			float fertility = 0f;
			float lowestFertility = 99f;
			float growth = 0f;

			for (int n = 0; n < numOfCells; ++n)
			{
				//Fertility calculations
				float fertilityHere = map.fertilityGrid.FertilityAt(zone.cells[n]);
				fertility += fertilityHere;
				if (fertilityHere < lowestFertility) lowestFertility = fertilityHere;

				//Plant tally
				Plant plant = map.thingGrid.ThingAt<Plant>(zone.cells[n]);
				if (plant != null && plant.def == zone.GetPlantDefToGrow())
				{
					growth += plant.Growth;
					++numOfPlants;
					if (plant.Growth < 0.08f) ++newPlants;
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
			
			while (simulatedGrowth < growthNeeded && simulatedGrowth != -1)
			{
				SimulateDay(numOfDays, ref simulatedGrowth, zone, tempOffsetCache, currentDay, plant.plant.dieIfLeafless, zoneData);

				if (++numOfDays > 120)
				{
					Log.Warning("[Smart Farming] failed simulating " + plant.defName + " at zone " + zone.Position);
					simulatedGrowth = -1;
				}
			}

			if (logging && Prefs.DevMode) Log.Message("[Smart Farming] simulation report: \n" + string.Join("\n", report));

			return simulatedGrowth == -1 ? -1 : (numOfDays * 60000) + Find.TickManager.TicksAbs;
		}

		void SimulateDay(int numOfDays, ref int simulatedGrowth, Zone_Growing zone, float tempOffset, int startingDay, bool sensitiveToCold, ZoneData zoneData)
		{
			int ticksOfLight = 32500; // 32500 = 60,000 ticks * .54167, only the hours this plant is "awake"
			
			//This adjusts the ticks of light if we're doing a partial day calculation, depending on what hour it currently is
			if (numOfDays == 0)
			{
				int hour = GenDate.HourOfDay(Find.TickManager.TicksGame ,Find.WorldGrid.LongLatOf(map.Tile).x);
				hour = 14 - hour;
				if (hour < 1)
				{
					if (hour < -3) hour = 13; //Past midnight, consider it a full day
					else hour = 0;
				}
				
				ticksOfLight = 2500 * hour;
			}

			//Prepare date
			numOfDays += startingDay;

			//Fertility
			float fertilityFactor = PlantUtility.GrowthRateFactorFor_Fertility(zone.GetPlantDefToGrow(), useAverageFertility ? zoneData.fertilityAverage : zoneData.fertilityLow);
			int growthToday =  (int)(ticksOfLight * fertilityFactor);

			//Temperature
			float low = Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, (int)(numOfDays * 60000) + 15000) + tempOffset;
			float high = Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, (int)(numOfDays * 60000) + 47500) + tempOffset;
			float average = (low + high) / 2f;
			growthToday = (int)(growthToday * PlantUtility.GrowthRateFactorFor_Temperature(average));
			
			//Results, use -1 if the plan will die/never grow
			simulatedGrowth = (fertilityFactor == 0 || (sensitiveToCold && Math.Min(low, high) < minTempAllowed)) ? -1 : simulatedGrowth + growthToday;

			//Debug
			if (logging && Prefs.DevMode)
			{
				report.Add(" - day: " + GenDate.DayOfYear(numOfDays * 60000, Find.WorldGrid.LongLatOf(map.Tile).x) + 
					" | temperature: " + Math.Round(low, 2) + " to " + Math.Round(high, 2) +  
					" | growth: " + simulatedGrowth.ToString() + 
					" | fertility: " + fertilityFactor.ToStringPercent() + 
					" | temperature: " + PlantUtility.GrowthRateFactorFor_Temperature(average).ToStringPercent());
			}
		}

		void CalculateYield(Zone_Growing zone)
		{
			//Reset
			growZoneRegistry[zone.ID].nutritionYield = 0f;

			//Fetch plant
			ThingDef plant = zone.GetPlantDefToGrow();
			if (plant == null) return;

			//Fetch plant's produce
			ThingDef produce = plant.plant.harvestedThingDef;
			if (produce == null) return;

			//Calculate the yield
			float num = plant.plant.harvestYield * Find.Storyteller.difficulty.cropYieldFactor * zone.cells.Count;
			float nutrition = produce.GetStatValueAbstract(StatDefOf.Nutrition, null);
			growZoneRegistry[zone.ID].nutritionYield = nutrition * num;
		}

		public void CalculateTotalHungerRate()
		{
			totalHungerRate = 0; //Reset
			List<Pawn> pawns = map.mapPawns.FreeColonistsAndPrisoners;
			foreach (Pawn pawn in pawns)
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

		public void ProcessZones()
		{
			map.zoneManager.AllZones.ForEach
			(x => 
				{
					Zone_Growing zone = x as Zone_Growing;
					if (zone != null) CalculateAll(zone);
				}
			);
		}

		public void CalculateAll(Zone_Growing zone)
		{
			ZoneData zoneData = growZoneRegistry[zone.ID];
			tempOffsetCache = map.gameConditionManager.AggregateTemperatureOffset();
			currentDay = GenDate.DayOfYear(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);
			
			CalculateAverages(zone, zoneData);
			zoneData.minHarvestDay = CalculateDaysToHarvest(zone, zoneData, false);
			zoneData.minHarvestDayForNewlySown = CalculateDaysToHarvest(zone, zoneData, true);
			CalculateYield(zone);
			CalculateTotalHungerRate();
		}

		public void HarvestNow(Zone_Growing zone)
		{
			ThingDef crop = zone.GetPlantDefToGrow();
			foreach (var cell in zone.cells)
			{
				Plant plant = zone.Map?.thingGrid.ThingAt<Plant>(cell);
				if (plant?.def == crop && plant.Growth >= crop.plant.harvestMinGrowth) plant.Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
			}
		}

		int ticks, currentDay;
		public Dictionary<int, ZoneData> growZoneRegistry = new Dictionary<int, ZoneData>();
		public float totalHungerRate, tempOffsetCache;
		List<string> report = new List<string>();
	}
}