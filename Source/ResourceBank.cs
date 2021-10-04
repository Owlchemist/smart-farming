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
	}
}