using Verse;
using Verse.Sound;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using System;
using static SmartFarming.ResourceBank;
 
namespace SmartFarming
{
	public class ZoneData : IExposable
	{
		public ZoneData()
		{
			fertilityAverage = 1f;
			priority = Priority.Normal;
		}
		public void ExposeData()
		{
			Scribe_Values.Look<SowMode>(ref sowMode, "sowMode", 0);
			Scribe_Values.Look<Priority>(ref priority, "priority", Priority.Normal);
			Scribe_Values.Look<float>(ref fertilityAverage, "averageFertility", 1f);
			Scribe_Values.Look<float>(ref fertilityLow, "fertilityLow", 1f);
			Scribe_Values.Look<float>(ref averageGrowth, "averageGrowth", 0f);
			Scribe_Values.Look<long>(ref minHarvestDay, "minHarvestDay", 0);
			Scribe_Values.Look<long>(ref minHarvestDayForNewlySown, "minHarvestDayForNewlySown", 0);
			Scribe_Values.Look<float>(ref nutritionYield, "nutritionYield", 0);
			Scribe_Values.Look<bool>(ref noPettyJobs, "pettyJobs");
			Scribe_Values.Look<bool>(ref allowHarvest, "allowHarvest", true);
			Scribe_Values.Look<bool>(ref orchardAlignment, "orchardAlignment");
		}
		public void Init(MapComponent_SmartFarming comp, Zone_Growing zone)
		{
			sowGizmo = new Command_Action()
				{
					hotKey = KeyBindingDefOf.Command_ItemForbid,
					icon = sowIconOn,
					action = () => SwitchSowMode(comp, zone)
				};
			priorityGizmo = new Command_Action()
				{
					icon = ResourceBank.iconPriority,
					defaultDesc = "SmartFarming.Icon.Priority.Desc".Translate(),
					action = () => SwitchPriority()
				};
			pettyJobsGizmo = new Command_Toggle
				{
					defaultLabel = "SmartFarming.Icon.NoPettyJobs".Translate(),
					defaultDesc = "SmartFarming.Icon.NoPettyJobs.Desc".Translate(),
					icon = TexCommand.ForbidOff,
					isActive = (() => noPettyJobs),
					toggleAction = delegate()
					{
						noPettyJobs = !noPettyJobs;
					}
				};
			allowHarvestGizmo = new Command_Toggle
				{
					defaultLabel = "SmartFarming.Icon.AllowHarvest".Translate(),
					defaultDesc = "SmartFarming.Icon.AllowHarvest.Desc".Translate(),
					icon = ResourceBank.allowHarvest,
					isActive = (() => allowHarvest),
					toggleAction = delegate()
					{
						allowHarvest = !allowHarvest;
					}
				};
			harvestGizmo = new Command_Action()
				{
					defaultLabel = "SmartFarming.Icon.HarvestNow".Translate(),
					defaultDesc = "SmartFarming.Icon.HarvestNow.Desc".Translate(),
					icon = ResourceBank.iconHarvest,
					action = () => comp.HarvestNow(zone, roofCheck: false)
				};
			orchardGizmo = new Command_Toggle
				{
					defaultLabel = "SmartFarming.Icon.OrchardAlignment".Translate(),
					defaultDesc = "SmartFarming.Icon.OrchardAlignment.Desc".Translate(),
					icon = ResourceBank.orchardAlignment,
					isActive = (() => orchardAlignment),
					toggleAction = delegate()
					{
						orchardAlignment = !orchardAlignment;
					}
				};
			UpdateGizmos();
			CalculateCornerCell(zone);
		}
		public void SwitchSowMode(MapComponent_SmartFarming comp, Zone_Growing zone, SowMode? hardSet = null)
		{
			SoundDefOf.Click.PlayOneShotOnCamera(null);
			if (hardSet != null) sowMode = hardSet.Value;

			switch (sowMode)
			{
				case SowMode.Force:
					sowMode = SowMode.Off;
					zone.allowSow = false;
					break;
				case SowMode.On:
					sowMode = SowMode.Smart;
					comp.CalculateAll(zone);
					zone.allowSow = true;
					break;
				case SowMode.Smart:
					sowMode = SowMode.Force;
					zone.allowSow = true;
					break;
				default:
					sowMode = SowMode.On;
					zone.allowSow = true;
					break;
			}
			UpdateGizmos();
		}
		public void SwitchPriority(Priority? hardSet = null)
		{
			SoundDefOf.Click.PlayOneShotOnCamera(null);
			if (hardSet != null) priority = hardSet.Value;

			int length = Enum.GetValues(typeof(Priority)).Length;
			priority = priority != Priority.Critical ? ++priority : Priority.Low;
			UpdateGizmos();
		}
		void UpdateGizmos()
		{
			sowGizmo.defaultLabel = ("SmartFarming.Icon." + sowMode.ToString()).Translate();
			sowGizmo.defaultDesc = ("SmartFarming.Icon." + sowMode.ToString() + ".Desc").Translate();
			sowGizmo.icon = iconCache[sowMode];

			priorityGizmo.defaultLabel = ("SmartFarming.Icon." + priority.ToString()).Translate();
		}
		public void CalculateCornerCell(Zone_Growing zone)
		{
			int southMost = Int16.MaxValue, westMost = Int16.MaxValue;
			foreach (var cell in zone.cells)
			{
				if (cell.x < southMost) southMost = cell.x;
				if (cell.z < westMost) westMost = cell.z;
			}
			this.cornerCell = new IntVec3(southMost, 0 , westMost);
		}
		
		public Priority priority; public enum Priority { Low = 1, Normal, Preferred, Important, Critical}
		public SowMode sowMode; public enum SowMode { On, Off, Smart, Force }
		public Dictionary<SowMode, Texture2D> iconCache = new Dictionary<SowMode, Texture2D>()
		{ 
			{SowMode.On, ResourceBank.sowIconOn},
			{SowMode.Off, ResourceBank.sowIconOff},
			{SowMode.Force, ResourceBank.sowIconForce},
			{SowMode.Smart, ResourceBank.sowIconSmart}
		};
		public float fertilityAverage, fertilityLow, averageGrowth, nutritionYield, nutritionCache;
		public long minHarvestDay, minHarvestDayForNewlySown;
		public bool noPettyJobs, allowHarvest = true, alwaysSow = false, orchardAlignment;
		public Command_Action sowGizmo = default(Command_Action);
		public Command_Action priorityGizmo = default(Command_Action);
		public Command_Toggle pettyJobsGizmo = default(Command_Toggle);
		public Command_Toggle allowHarvestGizmo = default(Command_Toggle);
		public Command_Action harvestGizmo = default(Command_Action);
		public Command_Toggle orchardGizmo = default(Command_Toggle);
		public IntVec3 cornerCell;
	}
}