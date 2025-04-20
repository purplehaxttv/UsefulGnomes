using HarmonyLib;
using UnityEngine;
using UsefulGnomes;

[HarmonyPatch(typeof(EnemyGnome), "UpdateState")]
public class Patch_EnemyGnome_UpdateState
{
    static bool Prefix(EnemyGnome __instance, ref EnemyGnome.State _state)
    {
        // Skip or reroute combat states
        if (_state == EnemyGnome.State.NoticeDelay ||
            _state == EnemyGnome.State.Notice ||
            _state == EnemyGnome.State.AttackMove ||
            _state == EnemyGnome.State.Attack ||
            _state == EnemyGnome.State.AttackDone)
        {
            // Optional: log state suppression for testing
            // Debug.Log($"Blocked transition to {_state}, rerouting to Idle or Move.");

            // Fallback to a safe non-aggressive state
            _state = EnemyGnome.State.Move;
        }

        return true; // Allow execution of UpdateState with potentially modified _state
    }
}
[HarmonyPatch(typeof(EnemyGnome), "OnSpawn")]
public class Patch_EnemyGnome_OnSpawn
{
    static void Postfix(EnemyGnome __instance)
    {
        if (__instance != null)
        {
            UsefulGnomesPlugin.Logger.LogInfo($">>> Gnome spawned at: {__instance.transform.position}");
        }
    }
}