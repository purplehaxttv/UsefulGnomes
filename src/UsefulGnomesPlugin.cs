using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace UsefulGnomes
{
    [BepInPlugin("com.purplehax.usefulgnomes", "Useful Gnomes", "1.0.0")]
    public class UsefulGnomesPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("Useful Gnomes loaded!");

            Harmony harmony = new Harmony("com.purplehax.usefulgnomes");
            harmony.PatchAll();
        }
    }
}
