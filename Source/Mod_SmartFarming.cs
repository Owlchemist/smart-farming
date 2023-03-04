using Verse;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using static SmartFarming.ModSettings_SmartFarming;
 
namespace SmartFarming
{
	[StaticConstructorOnStartup]
	public static class Setup
	{
        static Setup()
        {
            Mod_SmartFarming.agriWorkTypes = DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(
				x => x.defName == "GrowerHarvest" || x.defName == "GrowerSow").Select(y => y.index).ToHashSet();
        }
    }
    public class Mod_SmartFarming : Mod
	{
		//Int is the map ID
		public static Dictionary<int, MapComponent_SmartFarming> compCache = new Dictionary<int, MapComponent_SmartFarming>();
		//Keeps track of various worktypes that should be priority, like harvesting and soewing
		public static HashSet<ushort> agriWorkTypes = new HashSet<ushort>();

		public Mod_SmartFarming(ModContentPack content) : base(content)
		{
			base.GetSettings<ModSettings_SmartFarming>();
			new Harmony("owlchemist.smartfarming").PatchAll();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var buffer = processedFoodFactor.ToString();
			Listing_Standard options = new Listing_Standard();
			options.Begin(inRect);
			options.CheckboxLabeled("SmartFarming.Settings.AutoHarvestNow".Translate(), ref autoHarvestNow, "SmartFarming.Settings.AutoHarvestNow.Desc".Translate());
			options.CheckboxLabeled("SmartFarming.Settings.AutoCutBlighted".Translate(), ref autoCutBlighted, "SmartFarming.Settings.AutoCutBlighted.Desc".Translate());
			options.CheckboxLabeled("SmartFarming.Settings.AutoCutDying".Translate(), ref autoCutDying, "SmartFarming.Settings.AutoCutDying.Desc".Translate());
			options.CheckboxLabeled("SmartFarming.Settings.ColdSowing".Translate(), ref coldSowing, "SmartFarming.Settings.ColdSowing.Desc".Translate());
			options.CheckboxLabeled("SmartFarming.Settings.AllowHarvest".Translate(), ref allowHarvestOption, "SmartFarming.Settings.AllowHarvest.Desc".Translate());
			options.CheckboxLabeled("SmartFarming.Settings.OrchardAlignment".Translate(), ref orchardAlignment, "SmartFarming.Settings.OrchardAlignment.Desc".Translate());
			options.Gap();
			options.Label("SmartFarming.Settings.PettyJobsSlider".Translate("20%", "1%", "100%") + pettyJobs.ToStringPercent(), -1f, "SmartFarming.Settings.PettyJobs".Translate());
			pettyJobs = options.Slider(pettyJobs, 0.01f, 1f);
			
			options.Gap();
			options.Label("SmartFarming.Settings.SmartSowLabel".Translate());
			options.GapLine(); //======================================
			options.CheckboxLabeled("SmartFarming.Settings.UseAverageFertility".Translate(), ref useAverageFertility, "SmartFarming.Settings.UseAverageFertility.Desc".Translate());
			options.Gap();
			options.Label("SmartFarming.Settings.MinTempSlider".Translate("-3C", "-10C", "5C") + Math.Round(minTempAllowed, 1), -1f, "SmartFarming.Settings.MinTemp".Translate());
			minTempAllowed = options.Slider(minTempAllowed, -10f, 5f);
			options.Gap();
			options.GapLine(); //======================================
			options.Label("SmartFarming.Settings.ProcessedFoodLabel".Translate());
			options.TextFieldNumeric<float>(ref processedFoodFactor, ref buffer, 0f, 99f);
			options.Label("SmartFarming.Settings.ProcessedFood.Desc".Translate());
			if (Prefs.DevMode) options.CheckboxLabeled("DevMode: Enable logging", ref logging, null);
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
			try
			{
				if (Current.ProgramState == ProgramState.Playing) Find.Maps.ForEach(x => x.GetComponent<MapComponent_SmartFarming>()?.ProcessZones());	
			}
			catch (System.Exception ex)
			{                
				Log.Error("[Smart Farming] Error registering new grow zone:\n" + ex);
			}
		}
	}

	public class ModSettings_SmartFarming : ModSettings
	{
		public override void ExposeData()
		{
			Scribe_Values.Look(ref useAverageFertility, "useAverageFertility");
			Scribe_Values.Look(ref autoCutBlighted, "autoCutBlighted", true);
			Scribe_Values.Look(ref autoCutDying, "autoCutDying", true);
			Scribe_Values.Look(ref coldSowing, "coldSowing", true);
			Scribe_Values.Look(ref autoHarvestNow, "autoHarvestNow", true);
			Scribe_Values.Look(ref processedFoodFactor, "processedFoodFactor", 1.8f);
			Scribe_Values.Look(ref minTempAllowed, "minTempAllowed", -3f);
			Scribe_Values.Look(ref pettyJobs, "pettyJobs", 0.2f);
			Scribe_Values.Look(ref allowHarvestOption, "allowHarvestOption");
			Scribe_Values.Look(ref orchardAlignment, "orchardAlignment", true);

			base.ExposeData();
		}
		public static bool useAverageFertility, autoCutBlighted = true, autoCutDying = true, logging, coldSowing = true, autoHarvestNow = true, allowHarvestOption, orchardAlignment;
		public static float processedFoodFactor = 1.8f, pettyJobs = 0.2f, minTempAllowed = -3f;
	}
}
