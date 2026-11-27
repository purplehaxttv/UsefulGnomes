using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UsefulGnomes
{
    [BepInPlugin("com.purplehax.usefulgnomes", "Useful Gnomes", "2.0.0")]
    public class UsefulGnomesPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Logger;

        internal static ConfigEntry<bool> FetchEnabled;
        internal static ConfigEntry<float> FetchRange;
        internal static ConfigEntry<ValuableVolume.Type> MaxSize;
        internal static ConfigEntry<float> CarrySpeed;
        internal static ConfigEntry<float> IgnoreNearPlayer;
        internal static ConfigEntry<bool> Verbose;

        private void Awake()
        {
            Logger = base.Logger;
            transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            FetchEnabled = Config.Bind("Fetch", "Gnomes fetch valuables", true,
                "Gnomes pick up nearby valuables and carry them to the closest player.");
            FetchRange = Config.Bind("Fetch", "Fetch range", 30f,
                new ConfigDescription("How far from itself a gnome looks for something to fetch, in metres.", new AcceptableValueRange<float>(5f, 100f)));
            MaxSize = Config.Bind("Fetch", "Largest valuable", ValuableVolume.Type.Medium,
                "The biggest size of valuable a gnome will carry.");
            CarrySpeed = Config.Bind("Fetch", "Gnome speed", 3f,
                new ConfigDescription("How fast a gnome moves while fetching.", new AcceptableValueRange<float>(1f, 8f)));
            IgnoreNearPlayer = Config.Bind("Fetch", "Leave alone near players", 4f,
                new ConfigDescription("Valuables this close to a player are left where they are, in metres.", new AcceptableValueRange<float>(1f, 20f)));
            Verbose = Config.Bind("Debug", "Verbose log", false, "Log every pick up and delivery.");

            new Harmony("com.purplehax.usefulgnomes").PatchAll();
            Logger.LogInfo("Useful Gnomes loaded!");
        }

        private void Update()
        {
            try
            {
                GnomeFetch.Tick();
            }
            catch (Exception e)
            {
                Logger.LogError($"Fetch tick failed: {e}");
                GnomeFetch.Reset();
            }
        }

        private void FixedUpdate()
        {
            try
            {
                GnomeFetch.FixedTick();
            }
            catch (Exception e)
            {
                Logger.LogError($"Fetch physics failed: {e}");
                GnomeFetch.Reset();
            }
        }
    }
}
