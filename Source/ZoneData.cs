using UnityEngine;
using Verse;
using static SmartFarming.ResourceBank;
 
namespace SmartFarming
{
	public class ZoneData : IExposable
	{
		public ZoneData()
		{
			sowMode = SowMode.On;
			fertilityAverage = 1f;
			averageGrowth = 0f;
			minHarvestDay = 0;
			noPettyJobs = false;
			nutritionYield = 0f;
			iconCache = sowIconOn;
			priority = Priority.Normal;
		}

		public void ExposeData()
		{
			Scribe_Values.Look<SowMode>(ref sowMode, "sowMode", 0, false);
			Scribe_Values.Look<Priority>(ref priority, "priority", Priority.Normal, false);
			Scribe_Values.Look<float>(ref fertilityAverage, "averageFertility", 1f, false);
			Scribe_Values.Look<float>(ref averageGrowth, "averageGrowth", 0f, false);
			Scribe_Values.Look<long>(ref minHarvestDay, "minHarvestDay", 0, false);
			Scribe_Values.Look<long>(ref minHarvestDayForNewlySown, "minHarvestDayForNewlySown", 0, false);
			Scribe_Values.Look<float>(ref nutritionYield, "nutritionYield", 0, false);
			Scribe_Values.Look<bool>(ref noPettyJobs, "pettyJobs", false, false);

			switch (sowMode)
			{
				case SowMode.On:
					iconCache = sowIconOn;
					break;
				case SowMode.Off:
					iconCache = sowIconOff;
					break;
				case SowMode.Smart:
					iconCache = sowIconSmart;
					break;
				default:
					iconCache = sowIconForce;
					break;
			}
		}
		public Priority priority; public enum Priority { Low = 1, Normal, Preferred, Important, Critical}	
		public SowMode sowMode; public enum SowMode { On, Off, Smart, Force }
		public Texture2D iconCache;
		public float fertilityAverage, fertilityLow, averageGrowth, nutritionYield;
		public long minHarvestDay, minHarvestDayForNewlySown;
		public bool noPettyJobs;
	}
}