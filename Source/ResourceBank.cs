using UnityEngine;
using Verse;
using RimWorld;
 
namespace SmartFarming
{
	[StaticConstructorOnStartup]
	public static class ResourceBank
	{
		public static readonly Texture2D sowIconOn = ContentFinder<Texture2D>.Get("UI/Owl_Sow", true),
			sowIconOff = ContentFinder<Texture2D>.Get("UI/Owl_NoSow", true),
			sowIconSmart = ContentFinder<Texture2D>.Get("UI/Owl_SmartSow", true),
			sowIconForce = ContentFinder<Texture2D>.Get("UI/Owl_ForceSow", true),
			iconPriority = ContentFinder<Texture2D>.Get("UI/Owl_Priority", true),
			allowHarvest = ContentFinder<Texture2D>.Get("UI/Owl_AllowHarvest", true),
			iconHarvest = ContentFinder<Texture2D>.Get("UI/Designators/Harvest", true),
			orchardAlignment = ContentFinder<Texture2D>.Get("UI/Owl_Orchard", true),
			mergeZones = ContentFinder<Texture2D>.Get("UI/Owl_MergeZones", true);

		public static readonly string minHarvestDay = "SmartFarming.Inspector.MinHarvestDay".Translate(),
			minHarvestDayFail = "SmartFarming.Inspector.MinHarvestDayFail".Translate(),
			yield = "SmartFarming.Inspector.Yield".Translate();

		public static readonly Color white = Color.white, grey = Color.grey, green = Color.green, yellow = Color.yellow, red = Color.red;
	}
}