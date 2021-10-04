using UnityEngine;
using Verse;
using static SmartFarming.ResourceBank;
 
namespace SmartFarming
{
	public class ZoneData : IExposable
	{
		public ZoneData()
		{
			this.sowMode = SowMode.On;
			this.fertilityAverage = 1f;
			this.averageGrowth = 0f;
			this.minHarvestDay = 0;
			this.noPettyJobs = false;
			this.nutritionYield = 0f;
			this.iconCache = sowIconOn;
		}

		public void ExposeData()
		{
			Scribe_Values.Look<SowMode>(ref sowMode, "sowMode", 0, false);
			Scribe_Values.Look<float>(ref fertilityAverage, "averageFertility", 1f, false);
			Scribe_Values.Look<float>(ref averageGrowth, "averageGrowth", 0f, false);
			Scribe_Values.Look<long>(ref minHarvestDay, "minHarvestDay", 0, false);
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
		public SowMode sowMode;
		public enum SowMode { On, Off, Smart, Force }
		public float fertilityAverage;
		public float fertilityLow;
		public float averageGrowth;
		public long minHarvestDay;
		public bool noPettyJobs;
		public float nutritionYield;
		public Texture2D iconCache;
	}
}