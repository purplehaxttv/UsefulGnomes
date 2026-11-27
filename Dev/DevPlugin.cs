using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace UsefulGnomesDev
{
    // Not shipped. Starts a solo run, spawns gnomes next to the player and logs how many valuables end up nearby.
    [BepInPlugin("purplehaxttv.UsefulGnomesDev", "Useful Gnomes Dev", "0.0.1")]
    [BepInDependency("com.purplehax.usefulgnomes")]
    [BepInDependency("REPOLib")]
    public class DevPlugin : BaseUnityPlugin
    {
        static ManualLogSource L;

        void Awake()
        {
            L = Logger;
            transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            StartCoroutine(Run());
        }

        static void Say(string s) => L.LogInfo("DEVTEST " + s);

        IEnumerator Run()
        {
            while (FindObjectOfType<MenuPageMain>() == null) yield return new WaitForSecondsRealtime(1f);
            yield return new WaitForSecondsRealtime(6f);
            try { SemiFunc.MenuActionSingleplayerGame(); } catch (Exception e) { Say("start failed " + e); }
            yield return new WaitForSecondsRealtime(5f);
            float deadline = Time.realtimeSinceStartup + 180f;
            while (!(SemiFunc.RunIsLevel() && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated && FindObjectsOfType<PhysGrabCart>().Length > 0))
            {
                if (Time.realtimeSinceStartup > deadline) { Say("FAIL level never generated"); Application.Quit(); yield break; }
                yield return new WaitForSecondsRealtime(1f);
            }
            yield return new WaitForSecondsRealtime(12f);

            Transform me = PlayerAvatar.instance.transform;
            ValuableObject[] all = FindObjectsOfType<ValuableObject>();
            Say($"level ready, {all.Length} valuables, nearest {all.Select(v => Vector3.Distance(v.transform.position, me.position)).DefaultIfEmpty(-1).Min():F1}m, within 30m: {all.Count(v => Vector3.Distance(v.transform.position, me.position) < 30f)}");

            EnemySetup setup = REPOLib.Modules.Enemies.AllEnemies.FirstOrDefault(e => e && e.name == "Enemy - Gnome");
            List<EnemyParent> gnomes = setup ? REPOLib.Modules.Enemies.SpawnEnemy(setup, me.position + me.forward * 2.5f, Quaternion.identity, false) : null;
            Say("spawned gnomes: " + (gnomes?.Count ?? 0));

            for (int i = 0; i < 18; i++)
            {
                yield return new WaitForSecondsRealtime(10f);
                all = FindObjectsOfType<ValuableObject>();
                var near = all.Where(v => v && Vector3.Distance(v.transform.position, me.position) < 5f).ToList();
                string where = gnomes == null ? "" : string.Join(" ", gnomes.Where(g => g).Select(g => { var b = g.GetComponentInChildren<EnemyRigidbody>(true); return b ? Vector3.Distance(b.transform.position, me.position).ToString("F0") + "m" : "?"; }));
                Say($"t={(i + 1) * 10}s valuables within 5m of player: {near.Count} [{string.Join(", ", near.Select(v => v.name.Replace("Valuable ", "").Replace("(Clone)", "")))}] gnome distances: {where}");
                if (i == 8) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.BepInExRootPath, "gnomes.png"));
            }
            Say("DONE");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
