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

        static readonly HarmonyLib.AccessTools.FieldRef<ValuableObject, float> Value =
            HarmonyLib.AccessTools.FieldRefAccess<ValuableObject, float>("dollarValueCurrent");
        static readonly HarmonyLib.AccessTools.FieldRef<EnemyHealth, int> Hp =
            HarmonyLib.AccessTools.FieldRefAccess<EnemyHealth, int>("healthCurrent");
        float startValue, lostValue;
        int hits, destroyed;

        // Logs every valuable that loses value or is destroyed, and every gnome that loses health.
        IEnumerator Watch(List<EnemyParent> gnomes, Transform me)
        {
            var values = new Dictionary<ValuableObject, float>();
            var names = new Dictionary<ValuableObject, string>();
            var hp = new Dictionary<EnemyHealth, int>();
            foreach (ValuableObject v in FindObjectsOfType<ValuableObject>()) { values[v] = Value(v); names[v] = v.name; startValue += Value(v); }
            Vector3 start = me.position;
            float nextInfo = 0f;
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                // keep the level's own monsters out of the test
                foreach (EnemyParent other in EnemyDirector.instance.enemiesSpawned)
                {
                    if (!other || other.GetComponentInChildren<EnemyGnome>(true)) continue;
                    if (HarmonyLib.Traverse.Create(other).Field<bool>("Spawned").Value)
                    {
                        Say($"sending away {other.enemyName}");
                        other.SpawnedTimerSet(0f);
                    }
                    other.DespawnedTimerSet(600f);
                }
                if (Time.realtimeSinceStartup >= nextInfo)
                {
                    nextInfo = Time.realtimeSinceStartup + 10f;
                    PhysGrabCart cart = FindObjectsOfType<PhysGrabCart>().OrderBy(c => Vector3.Distance(c.transform.position, me.position)).FirstOrDefault();
                    Say($"player moved {Vector3.Distance(start, me.position):F1}m, hp {HarmonyLib.Traverse.Create(PlayerAvatar.instance.playerHealth).Field<int>("health").Value}, nearest cart {(cart ? Vector3.Distance(cart.transform.position, me.position) : -1f):F1}m");
                }
                foreach (ValuableObject v in values.Keys.ToList())
                {
                    if (!v)
                    {
                        destroyed++; lostValue += values[v];
                        Say($"DESTROYED {names[v]} (was ${values[v]:F0})");
                        values.Remove(v);
                        continue;
                    }
                    float now = Value(v);
                    if (now < values[v] - 0.5f)
                    {
                        hits++; lostValue += values[v] - now;
                        float gnome = gnomes == null ? -1 : gnomes.Where(g => g).Select(g => g.GetComponentInChildren<EnemyRigidbody>(true)).Where(b => b).Select(b => Vector3.Distance(b.transform.position, v.transform.position)).DefaultIfEmpty(-1).Min();
                        Say($"HIT {v.name} ${values[v]:F0} -> ${now:F0}, {Vector3.Distance(v.transform.position, me.position):F1}m from player, nearest gnome {gnome:F1}m");
                    }
                    values[v] = now;
                }
                foreach (EnemyParent g in gnomes ?? new List<EnemyParent>())
                {
                    EnemyHealth h = g ? g.GetComponentInChildren<EnemyHealth>(true) : null;
                    if (!h) continue;
                    int cur = Hp(h);
                    if (hp.TryGetValue(h, out int before) && cur < before) Say($"GNOME HURT {before} -> {cur}");
                    hp[h] = cur;
                }
            }
        }

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

            // our own loot, so every run has the same amount to fetch
            var small = REPOLib.Modules.Valuables.AllValuables
                .Where(r => { try { var v = r.Prefab ? r.Prefab.GetComponent<ValuableObject>() : null; return v && v.volumeType <= ValuableVolume.Type.Medium; } catch { return false; } })
                .OrderBy(r => r.PrefabName).ToList();
            int placed = 0;
            for (int n = 0; n < 48 && placed < 14 && small.Count > 0; n++)
            {
                Vector3 dir = Quaternion.Euler(0f, n * 47f, 0f) * Vector3.forward;
                Vector3 want = me.position + dir * (7f + (n % 4) * 2f);
                if (!UnityEngine.AI.NavMesh.SamplePosition(want, out UnityEngine.AI.NavMeshHit hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (Vector3.Distance(hit.position, me.position) < 6f || Mathf.Abs(hit.position.y - me.position.y) > 1f) continue;
                if (REPOLib.Modules.Valuables.SpawnValuable(small[(placed * 7) % small.Count], hit.position + Vector3.up * 0.5f, Quaternion.identity)) placed++;
            }
            Say($"placed {placed} valuables 6 to 14m from the player");
            yield return new WaitForSecondsRealtime(6f);

            foreach (EnemyParent g in gnomes ?? new List<EnemyParent>())
            {
                EnemyHealth h = g ? g.GetComponentInChildren<EnemyHealth>(true) : null;
                if (h) { Say($"gnome health {h.health}, impactHurt={h.impactHurt} light/medium/heavy={h.impactLightDamage}/{h.impactMediumDamage}/{h.impactHeavyDamage}, objectHurt={h.objectHurt} x{h.objectHurtMultiplier}"); break; }
            }
            StartCoroutine(Watch(gnomes, me));

            for (int i = 0; i < 18; i++)
            {
                yield return new WaitForSecondsRealtime(10f);
                all = FindObjectsOfType<ValuableObject>();
                var near = all.Where(v => v && Vector3.Distance(v.transform.position, me.position) < 5f).ToList();
                string where = gnomes == null ? "" : string.Join(" ", gnomes.Where(g => g).Select(g => { var b = g.GetComponentInChildren<EnemyRigidbody>(true); return b ? Vector3.Distance(b.transform.position, me.position).ToString("F0") + "m" : "?"; }));
                Say($"t={(i + 1) * 10}s valuables within 5m of player: {near.Count} [{string.Join(", ", near.Select(v => v.name.Replace("Valuable ", "").Replace("(Clone)", "")))}] gnome distances: {where}");
                if (i == 8) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.BepInExRootPath, "gnomes.png"));
            }
            EnemyParent victim = gnomes?.FirstOrDefault(g => g && HarmonyLib.Traverse.Create(g).Field<bool>("Spawned").Value);
            EnemyHealth vh = victim ? victim.GetComponentInChildren<EnemyHealth>(true) : null;
            if (vh)
            {
                object enemy = HarmonyLib.Traverse.Create(vh).Field("enemy").GetValue();
                HarmonyLib.Traverse.Create(enemy).Field("StateStunned").Method("Set", 4f).GetValue();
                yield return new WaitForSecondsRealtime(0.5f);
                int before = Hp(vh);
                vh.MediumImpact();
                vh.HeavyImpact();
                yield return new WaitForSecondsRealtime(0.5f);
                Say($"STURDY stunned gnome took a medium and a heavy impact: hp {before} -> {Hp(vh)}, impactHurt={vh.impactHurt} objectHurt={vh.objectHurt}");
            }
            else Say("STURDY no gnome left to test");
            Say($"SUMMARY value lost {lostValue:F0} of {startValue:F0}, hits {hits}, destroyed {destroyed}, gnomes alive {(gnomes == null ? 0 : gnomes.Count(g => g && HarmonyLib.Traverse.Create(g).Field<bool>("Spawned").Value))}/{gnomes?.Count ?? 0}");
            Say("DONE");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
