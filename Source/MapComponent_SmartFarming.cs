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
			map.zoneManager.AllZones.Where(x => x.GetType() == typeof(Zone_Growing)).ToList().ForEach(y => {
				if (!growZoneRegistry.ContainsKey(y.ID))
				{
					growZoneRegistry.Add(y.ID,new ZoneData());
					CalculateAll((Zone_Growing)y);
				}
			});
		}

		public void SwitchPriority(Zone_Growing zone)
		{
			SoundDefOf.Click.PlayOneShotOnCamera(null);
			
			var zoneData = growZoneRegistry[zone.ID];
			var length = Enum.GetValues(typeof(Priority)).Length;

			if (zoneData.priority != Priority.Critical) ++zoneData.priority;
			else zoneData.priority = Priority.Low;
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
		
		private void CalculateAverages(Zone_Growing zone)
		{
			List<IntVec3> cells = zone.Cells;
			int numOfCells = zone.cells.Count;
			int numOfPlants = 0;
			int newPlants = 0;
			float fertility = 0f;
			float lowestFertility = 99f;
			float growth = 0f;
			ZoneData zoneData = growZoneRegistry[zone.ID];

			for (int n = 0; n < numOfCells; n++)
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
					numOfPlants++;
					if (plant.Growth < 0.08f) newPlants++;
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
				if (validPlants > 0 && 1 - (validPlants / (float)numOfCells) < pettyJobs) zone.allowSow = false;
				else zone.allowSow = true;
			}
		}

		private void CalculateDaysToHarvest(Zone_Growing zone, bool forSowing = false)
		{
			ZoneData zoneData = growZoneRegistry[zone.ID];

			//Check for toxic fallout first
			if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout) && !map.roofGrid.Roofed(zone.Position)){
				zoneData.minHarvestDay = -1;
				return;
			}

			ThingDef plant = zone.GetPlantDefToGrow();
			if (plant == null) return;

			//Prepare variables
			
			int growthNeeded = (int)(plant.plant.growDays * plant.plant.harvestMinGrowth * 60000f * 1.1f * (1f - (forSowing ? 0f : zoneData.averageGrowth / plant.plant.harvestMinGrowth)));
			//Log.Message(plant.plant.growDays.ToString() + " * " + plant.plant.harvestMinGrowth.ToString() + " * 60000 * 1.1 * " + (1f - (zoneData.averageGrowth / plant.plant.harvestMinGrowth)).ToString() + " = " + growthNeeded.ToString());
			int simulatedGrowth = 0;
			int numOfDays = 0;
			int startingDay = GenDate.DayOfYear(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);
			//Log.Message("Starting day is: " + startingDay.ToString());

			//Run simulation
			float tempOffset = map.gameConditionManager.AggregateTemperatureOffset();
			Resimulate:
			SimulateDay(numOfDays, ref simulatedGrowth, zone, tempOffset, startingDay, plant.plant.dieIfLeafless);
			//Failsafe... if a map never freezes and a plant never grows for some reason.
			if (numOfDays > 120){
				Log.Warning("[Smart Farming] failed simulating " + plant.defName + " at zone " + zone.Position);
				simulatedGrowth = -1;
			}
			if (simulatedGrowth < growthNeeded && simulatedGrowth != -1) {numOfDays++; goto Resimulate;}

			//Use results
			if (forSowing)
			{
				if (simulatedGrowth == -1) zoneData.minHarvestDayForNewlySown = -1;
				else zoneData.minHarvestDayForNewlySown = (numOfDays * 60000) + Find.TickManager.TicksAbs;
			}
			else
			{
				if (simulatedGrowth == -1) zoneData.minHarvestDay = -1;
				else zoneData.minHarvestDay = (numOfDays * 60000) + Find.TickManager.TicksAbs;
			}
			
		}

		private void SimulateDay(int numOfDays, ref int simulatedGrowth, Zone_Growing zone, float tempOffset, int startingDay, bool sensitiveToCold)
		{
			ZoneData zoneData = growZoneRegistry[zone.ID];

			int ticksOfLight = 32500; // 32500 = 60,000 ticks * .54167, only the hours this plant is "awake"
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
			int growthToday =  (int)(ticksOfLight * PlantUtility.GrowthRateFactorFor_Fertility(zone.GetPlantDefToGrow(), useAverageFertility ? zoneData.fertilityAverage : zoneData.fertilityLow));

			//Temperature
			float low = Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, (int)(numOfDays * 60000) + 15000) + tempOffset;
			float high = Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, (int)(numOfDays * 60000) + 47500) + tempOffset;
			float average = (low + high) / 2f;
			growthToday = (int)(growthToday * PlantUtility.GrowthRateFactorFor_Temperature(average));
			
			//Results
			simulatedGrowth = (sensitiveToCold && low < minTempAllowed) ? -1 : simulatedGrowth + growthToday; //Has froze?

			//Debug
			/*
			Log.Message("[Smart Farming] on day " + GenDate.DayOfYear(numOfDays * 60000, Find.WorldGrid.LongLatOf(map.Tile).x) + 
			" the temperature will be " + Math.Round(low, 2) + " - " + Math.Round(high, 2) + " (average: " + Math.Round(average, 2) + 
			") and simulated growth up to " + simulatedGrowth.ToString() + 
			" and fertility factor was " + PlantUtility.GrowthRateFactorFor_Fertility(zone.GetPlantDefToGrow(), useAverageFertility ? zoneData.fertilityAverage : zoneData.fertilityLow).ToStringPercent() + 
			" and temperature factor was " + PlantUtility.GrowthRateFactorFor_Temperature(average).ToStringPercent());
			*/
		}

		private void CalculateYield(Zone_Growing zone)
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
			var num = plant.plant.harvestYield * Find.Storyteller.difficulty.cropYieldFactor * zone.cells.Count;
			var nutrition = produce.GetStatValueAbstract(StatDefOf.Nutrition, null);
			growZoneRegistry[zone.ID].nutritionYield = nutrition * num;
		}

		public void CalculateTotalHungerRate()
		{
			totalHungerRate = 0; //Reset
			var pawns = map.mapPawns.FreeColonistsAndPrisoners;
			int length = pawns.Count;
			for (int i = 0; i < length; i++)
			{
				Pawn pawn = pawns[i];
				totalHungerRate += Need_Food.BaseHungerRateFactor(pawn.ageTracker.CurLifeStage, pawn.def) * pawn.health.hediffSet.HungerRateFactor * 
				((pawn.story == null || pawn.story.traits == null) ? 1f : pawn.story.traits.HungerRateFactor) * pawn.GetStatValue(StatDefOf.HungerRateMultiplier, true);
			}
		}

		public override void MapComponentTick()
		{
			if (ticks++ == 2500) //Hourly
			{
				ticks = 0;
				ProcessZones();
			}
		}

		public void ProcessZones()
		{
			List<Zone> zones = map.zoneManager.AllZones;
			var numOfZones = zones.Count;
			for (int i = 0; i < numOfZones; i++)
			{
				Zone_Growing zone = zones[i] as Zone_Growing;
				if (zone != null)
				{
					CalculateAll(zone);
				}
			}
		}

		public void CalculateAll(Zone_Growing zone)
		{
			CalculateAverages(zone);
			CalculateDaysToHarvest(zone);
			CalculateDaysToHarvest(zone, true);
			CalculateYield(zone);
			CalculateTotalHungerRate();
		}

		int ticks = 0;
		public Dictionary<int, ZoneData> growZoneRegistry = new Dictionary<int, ZoneData>();
		public float totalHungerRate;
	}
}