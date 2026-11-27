using HarmonyLib;
using UsefulGnomes;

[HarmonyPatch(typeof(EnemyGnome), "UpdateState")]
public class Patch_EnemyGnome_UpdateState
{
    static bool Prefix(EnemyGnome __instance, ref EnemyGnome.State _state)
    {
        if (_state == EnemyGnome.State.NoticeDelay ||
            _state == EnemyGnome.State.Notice ||
            _state == EnemyGnome.State.AttackMove ||
            _state == EnemyGnome.State.Attack ||
            _state == EnemyGnome.State.AttackDone)
        {
            _state = EnemyGnome.State.Move;
        }

        return true;
    }
}

// While a gnome is on a fetch job the mod steers it, so the game's own wandering is skipped.
[HarmonyPatch(typeof(EnemyGnome), "StateIdle")]
public class Patch_EnemyGnome_StateIdle
{
    static bool Prefix(EnemyGnome __instance) => !GnomeFetch.IsBusy(__instance);
}

[HarmonyPatch(typeof(EnemyGnome), "StateMove")]
public class Patch_EnemyGnome_StateMove
{
    static bool Prefix(EnemyGnome __instance) => !GnomeFetch.IsBusy(__instance);
}
