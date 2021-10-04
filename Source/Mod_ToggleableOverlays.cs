using Verse;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using static SmartFarming.ModSettings_SmartFarming;
 
namespace SmartFarming
{
    public class Mod_SmartFarming : Mod
	{
		public static Dictionary<Map, MapComponent_SmartFarming> compCache = new Dictionary<Map, MapComponent_SmartFarming>();

		public Mod_SmartFarming(ModContentPack content) : base(content)
		{
			new Harmony(this.Content.PackageIdPlayerFacing).PatchAll();
			base.GetSettings<ModSettings_SmartFarming>();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var buffer = processedFoodFactor.ToString();
			Listing_Standard options = new Listing_Standard();
			options.Begin(inRect);
			options.CheckboxLabeled("SmartFarm_AutoCutBlighted".Translate(), ref autoCutBlighted, "SmartFarm_AutoCutBlightedDesc".Translate());
			options.CheckboxLabeled("SmartFarm_AutoCutDying".Translate(), ref autoCutDying, "SmartFarm_AutoCutDyingDesc".Translate());
			options.Gap();
			options.Label("SmartFarm_PettyJobsSlider".Translate("20%", "1%", "100%") + pettyJobs.ToStringPercent(), -1f, "SmartFarm_PettyJobs".Translate());
			pettyJobs = options.Slider(pettyJobs, 0.01f, 1f);
			
			options.Gap();
			options.Label("SmartFarm_SmartSowLabel".Translate());
			options.GapLine(); //======================================
			options.CheckboxLabeled("SmartFarm_UseAverageFertility".Translate(), ref useAverageFertility, "SmartFarm_UseAverageFertilityDesc".Translate());
			options.Gap();
			options.Label("SmartFarm_MinTempSlider".Translate("-4C", "-10C", "5C") + Math.Round(minTempAllowed, 2), -1f, "SmartFarm_MinTemp".Translate());
			minTempAllowed = options.Slider(minTempAllowed, -10f, 5f);
			options.Gap();
			options.GapLine(); //======================================
			options.Label("SmartFarm_ProcessedFoodLabel".Translate());
			options.TextFieldNumeric<float>(ref processedFoodFactor, ref buffer, 0f, 99f);
			options.Label("SmartFarm_ProcessedFoodDesc".Translate());
			options.End();
			base.DoSettingsWindowContents(inRect);
		}

		public override string SettingsCategory()
		{
			return "Smart Farming";
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
			if (Find.CurrentMap != null)
			{
				foreach (var map in Find.Maps)
				{
					var comp = map.GetComponent<MapComponent_SmartFarming>();
					if (comp == null) continue;
					comp.ProcessZones();
				}
			}
		}
	}

	public class ModSettings_SmartFarming : ModSettings
	{
		public override void ExposeData()
		{
			Scribe_Values.Look<bool>(ref useAverageFertility, "useAverageFertility", false, false);
			Scribe_Values.Look<bool>(ref autoCutBlighted, "autoCutBlighted", true, false);
			Scribe_Values.Look<bool>(ref autoCutDying, "autoCutDying", true, false);
			Scribe_Values.Look<float>(ref processedFoodFactor, "processedFoodFactor", 1.8f, false);
			Scribe_Values.Look<float>(ref minTempAllowed, "minTempAllowed", -4f, false);
			Scribe_Values.Look<float>(ref pettyJobs, "pettyJobs", 0.2f, false);

			base.ExposeData();
		}
		public static bool useAverageFertility = false;
		public static bool autoCutBlighted = true;
		public static bool autoCutDying = true;
		public static float processedFoodFactor = 1.8f;
		public static float pettyJobs = 0.2f;
		public static float minTempAllowed = -4;
	}
}
