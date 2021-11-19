using UnityEngine;
using Verse;
 
namespace SmartFarming
{
	[StaticConstructorOnStartup]
	internal static class ResourceBank
	{
		public static readonly Texture2D sowIconOn = ContentFinder<Texture2D>.Get("UI/Owl_Sow", true);
		public static readonly Texture2D sowIconOff = ContentFinder<Texture2D>.Get("UI/Owl_NoSow", true);
		public static readonly Texture2D sowIconSmart = ContentFinder<Texture2D>.Get("UI/Owl_SmartSow", true);
		public static readonly Texture2D sowIconForce = ContentFinder<Texture2D>.Get("UI/Owl_ForceSow", true);
		public static readonly Texture2D iconPriority = ContentFinder<Texture2D>.Get("UI/Owl_Priority", true);
		public static readonly Texture2D iconHarvest =  ContentFinder<Texture2D>.Get("UI/Designators/Harvest", true);
	}
}