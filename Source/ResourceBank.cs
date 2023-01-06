using UnityEngine;
using Verse;
using RimWorld;
 
namespace SmartFarming
{
	[StaticConstructorOnStartup]
	public static class ResourceBank
	{
		public static readonly Texture2D sowIconOn = ContentFinder<Texture2D>.Get("UI/Owl_Sow", true);
		public static readonly Texture2D sowIconOff = ContentFinder<Texture2D>.Get("UI/Owl_NoSow", true);
		public static readonly Texture2D sowIconSmart = ContentFinder<Texture2D>.Get("UI/Owl_SmartSow", true);
		public static readonly Texture2D sowIconForce = ContentFinder<Texture2D>.Get("UI/Owl_ForceSow", true);
		public static readonly Texture2D iconPriority = ContentFinder<Texture2D>.Get("UI/Owl_Priority", true);
		public static readonly Texture2D allowHarvest =  ContentFinder<Texture2D>.Get("UI/Owl_AllowHarvest", true);
		public static readonly Texture2D iconHarvest =  ContentFinder<Texture2D>.Get("UI/Designators/Harvest", true);
		public static readonly Texture2D orchardAlignment =  ContentFinder<Texture2D>.Get("UI/Owl_Orchard", true);

		public static readonly string minHarvestDay = "SmartFarming.Inspector.MinHarvestDay".Translate();
		public static readonly string minHarvestDayFail = "SmartFarming.Inspector.MinHarvestDayFail".Translate();
		public static readonly string averageGrowth = "SmartFarming.Inspector.AverageGrowth".Translate();
		public static readonly string yield = "SmartFarming.Inspector.Yield".Translate();

		public static readonly Color white = Color.white;
		public static readonly Color grey = Color.grey;
		public static readonly Color green = Color.green;
		public static readonly Color yellow = Color.yellow;
		public static readonly Color red = Color.red;
	}
}