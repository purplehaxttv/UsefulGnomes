using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace UsefulGnomes
{
    // Host-side: sends idle gnomes to pick up valuables and carry them to the nearest player.
    internal static class GnomeFetch
    {
        enum Phase { ToItem, Carry, SetDown }

        sealed class Job
        {
            public EnemyGnome Gnome;
            public PhysGrabObject Item;
            public Phase Phase;
            public float PhaseTime;
            public PlayerAvatar Player;
            public float NextPlayerPick;
            public Vector3 ItemNavPoint;
            public Vector3 SetDownSpot;
        }

        // An item a gnome has let go of. It keeps passing through other loot until nothing overlaps it,
        // so it is never shoved out of another object.
        sealed class Settling
        {
            public PhysGrabObject Item;
            public float Until;
        }

        const float AssignInterval = 1f;
        const float ItemScanInterval = 3f;
        const float ReachItem = 1.4f;
        const float ReachPlayer = 2.2f;
        const float ToItemTimeout = 30f;
        const float CarryTimeout = 90f;
        const float SetDownTime = 0.7f;
        const float SetDownTimeout = 2.5f;
        const float SettleSeconds = 6f;
        const float SkipSeconds = 60f;

        static readonly Dictionary<EnemyGnome, Job> jobs = new Dictionary<EnemyGnome, Job>();
        static readonly HashSet<PhysGrabObject> claimed = new HashSet<PhysGrabObject>();
        static readonly HashSet<PhysGrabObject> delivered = new HashSet<PhysGrabObject>();
        static readonly Dictionary<PhysGrabObject, float> skipUntil = new Dictionary<PhysGrabObject, float>();
        static readonly Dictionary<EnemyGnome, float> restUntil = new Dictionary<EnemyGnome, float>();
        static readonly List<EnemyGnome> finished = new List<EnemyGnome>();
        static readonly Dictionary<PhysGrabObject, List<KeyValuePair<Collider, LayerMask>>> passing = new Dictionary<PhysGrabObject, List<KeyValuePair<Collider, LayerMask>>>();
        static readonly List<Settling> settling = new List<Settling>();
        static readonly Collider[] overlaps = new Collider[32];
        static ValuableObject[] valuables = new ValuableObject[0];
        static PhysGrabCart[] carts = new PhysGrabCart[0];
        static float nextAssign;
        static float nextItemScan;

        internal static bool IsBusy(EnemyGnome gnome) => jobs.ContainsKey(gnome);

        internal static void Reset()
        {
            foreach (Job job in jobs.Values) Release(job);
            jobs.Clear();
            foreach (PhysGrabObject item in new List<PhysGrabObject>(passing.Keys)) PassThrough(item, false);
            passing.Clear();
            settling.Clear();
            claimed.Clear();
            delivered.Clear();
            skipUntil.Clear();
            restUntil.Clear();
        }

        static bool Active()
        {
            return UsefulGnomesPlugin.FetchEnabled.Value
                && SemiFunc.IsMasterClientOrSingleplayer()
                && SemiFunc.RunIsLevel()
                && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated
                && EnemyGnomeDirector.instance != null && EnemyGnomeDirector.instance.setup;
        }

        internal static void Tick()
        {
            if (!Active())
            {
                if (jobs.Count > 0 || delivered.Count > 0 || passing.Count > 0) Reset();
                return;
            }

            float now = Time.time;
            Settle(now);
            if (now >= nextItemScan)
            {
                nextItemScan = now + ItemScanInterval;
                valuables = Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None);
                carts = Object.FindObjectsByType<PhysGrabCart>(FindObjectsSortMode.None);
            }

            finished.Clear();
            foreach (Job job in jobs.Values)
            {
                if (!Drive(job, now)) finished.Add(job.Gnome);
            }
            foreach (EnemyGnome gnome in finished)
            {
                if (jobs.TryGetValue(gnome, out Job job)) Release(job);
                jobs.Remove(gnome);
                if (gnome) restUntil[gnome] = now + 2f;
            }

            if (now < nextAssign) return;
            nextAssign = now + AssignInterval;
            if (UsefulGnomesPlugin.SturdyGnomes.Value) MakeSturdy();
            foreach (EnemyGnome gnome in EnemyGnomeDirector.instance.gnomes)
            {
                if (!Available(gnome, now)) continue;
                PhysGrabObject item = PickItem(gnome, now, out Vector3 navPoint);
                if (!item) continue;
                claimed.Add(item);
                jobs[gnome] = new Job { Gnome = gnome, Item = item, Phase = Phase.ToItem, ItemNavPoint = navPoint };
                Say($"{Name(gnome)} goes for {item.name}");
            }
        }

        internal static void FixedTick()
        {
            foreach (Job job in jobs.Values)
            {
                if (job.Phase == Phase.ToItem || !job.Item || !job.Gnome) continue;
                Hold(job);
            }
        }

        static bool Available(EnemyGnome gnome, float now)
        {
            if (!gnome || !gnome.isActiveAndEnabled || jobs.ContainsKey(gnome)) return false;
            if (!gnome.enemy || !gnome.enemy.EnemyParent || !gnome.enemy.EnemyParent.Spawned) return false;
            if (gnome.currentState != EnemyGnome.State.Idle && gnome.currentState != EnemyGnome.State.Move) return false;
            return !restUntil.TryGetValue(gnome, out float until) || now >= until;
        }

        static bool GnomeOk(EnemyGnome gnome)
        {
            return gnome && gnome.isActiveAndEnabled && gnome.enemy && gnome.enemy.EnemyParent && gnome.enemy.EnemyParent.Spawned
                && gnome.currentState != EnemyGnome.State.Stun && gnome.currentState != EnemyGnome.State.Despawn
                && gnome.currentState != EnemyGnome.State.Spawn && gnome.enemy.Rigidbody;
        }

        static PhysGrabObject PickItem(EnemyGnome gnome, float now, out Vector3 navPoint)
        {
            navPoint = Vector3.zero;
            Vector3 from = gnome.enemy.Rigidbody.transform.position;
            float range = UsefulGnomesPlugin.FetchRange.Value;
            float keepAway = UsefulGnomesPlugin.IgnoreNearPlayer.Value;
            List<PlayerAvatar> players = SemiFunc.PlayerGetAll();
            if (!AnyPlayerAlive(players)) return null;

            PhysGrabObject best = null;
            float bestDistance = range;
            foreach (ValuableObject valuable in valuables)
            {
                if (!valuable || !valuable.isActiveAndEnabled) continue;
                if (valuable.volumeType > UsefulGnomesPlugin.MaxSize.Value) continue;
                PhysGrabObject item = valuable.physGrabObject;
                if (!item || claimed.Contains(item) || delivered.Contains(item)) continue;
                if (skipUntil.TryGetValue(item, out float skip) && now < skip) continue;
                if (item.playerGrabbing.Count > 0) continue;

                float distance = Vector3.Distance(from, item.centerPoint);
                if (distance >= bestDistance) continue;
                if (NearAnyPlayer(players, item.centerPoint, keepAway)) continue;
                if (InCart(item)) continue;
                RoomVolumeCheck room = item.GetComponent<RoomVolumeCheck>();
                if (room && (room.inTruck || room.inExtractionPoint)) continue;
                if (!NavMesh.SamplePosition(item.centerPoint, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) continue;

                best = item;
                bestDistance = distance;
                navPoint = hit.position;
            }
            return best;
        }

        static bool Drive(Job job, float now)
        {
            EnemyGnome gnome = job.Gnome;
            PhysGrabObject item = job.Item;
            if (!GnomeOk(gnome) || !item || !item.isActiveAndEnabled)
            {
                if (item && job.Phase != Phase.ToItem && NearAnyPlayer(SemiFunc.PlayerGetAll(), item.centerPoint, 5f)) delivered.Add(item);
                Say($"{Name(gnome)} dropped its job ({(gnome ? gnome.currentState.ToString() : "gone")})");
                return false;
            }
            if (item.playerGrabbing.Count > 0)
            {
                delivered.Add(item);
                Say($"{Name(gnome)}: a player took {item.name}");
                return false;
            }

            job.PhaseTime += Time.deltaTime;
            Vector3 body = gnome.enemy.Rigidbody.transform.position;
            gnome.enemy.EnemyParent.SpawnedTimerPause(0.5f);
            if (gnome.currentState == EnemyGnome.State.Idle) gnome.UpdateState(EnemyGnome.State.Move);

            if (job.Phase == Phase.ToItem)
            {
                MoveTo(gnome, job.ItemNavPoint);
                Vector3 flat = item.centerPoint - body;
                float height = Mathf.Abs(flat.y);
                flat.y = 0f;
                if (flat.magnitude <= ReachItem && height <= 2.5f)
                {
                    job.Phase = Phase.Carry;
                    job.PhaseTime = 0f;
                    IgnoreGnomes(item);
                    PassThrough(item, true);
                    Say($"{Name(gnome)} picked up {item.name}");
                    return true;
                }
                if (job.PhaseTime > ToItemTimeout)
                {
                    skipUntil[item] = now + SkipSeconds;
                    Say($"{Name(gnome)} could not reach {item.name}");
                    return false;
                }
                return true;
            }

            if (job.Phase == Phase.Carry)
            {
                if (!job.Player || now >= job.NextPlayerPick || !Alive(job.Player))
                {
                    job.NextPlayerPick = now + 1f;
                    job.Player = NearestPlayer(body);
                }
                if (!job.Player) return job.PhaseTime <= CarryTimeout;

                Vector3 target = job.Player.transform.position;
                MoveTo(gnome, target);
                if (Vector3.Distance(body, target) <= ReachPlayer)
                {
                    job.Phase = Phase.SetDown;
                    job.PhaseTime = 0f;
                    job.SetDownSpot = PickSetDownSpot(job, body);
                    return true;
                }
                if (job.PhaseTime > CarryTimeout)
                {
                    skipUntil[item] = now + SkipSeconds;
                    Say($"{Name(gnome)} gave up carrying {item.name}");
                    return false;
                }
                return true;
            }

            gnome.enemy.NavMeshAgent.ResetPath();
            if (job.PhaseTime < SetDownTime) return true;
            if (job.PhaseTime < SetDownTimeout && Vector3.Distance(item.centerPoint, job.SetDownSpot) > 0.25f) return true;
            delivered.Add(item);
            Say($"{Name(gnome)} delivered {item.name}, {(job.Player ? Vector3.Distance(item.centerPoint, job.Player.transform.position) : 0f):F1}m from the player");
            return false;
        }

        static void MoveTo(EnemyGnome gnome, Vector3 target)
        {
            float speed = UsefulGnomesPlugin.CarrySpeed.Value;
            gnome.enemy.NavMeshAgent.OverrideAgent(speed, speed * 4f, 0.2f);
            gnome.enemy.NavMeshAgent.SetDestination(target);
            SemiFunc.EnemyCartJump(gnome.enemy);
        }

        static void Hold(Job job)
        {
            PhysGrabObject item = job.Item;
            Rigidbody rb = item.rb;
            if (!rb) return;
            Transform body = job.Gnome.enemy.Rigidbody.transform;
            float halfHeight = Mathf.Max(item.boundingBox.y, 0.1f) * 0.5f;

            bool carrying = job.Phase == Phase.Carry;
            Vector3 hold = carrying ? body.position + Vector3.up * (0.55f + halfHeight) : job.SetDownSpot;

            item.OverrideZeroGravity(0.2f);
            item.OverrideIndestructible(1.5f);
            // Follow the gnome closely while it walks, but come down slowly when being set down.
            Vector3 velocity = (hold - item.centerPoint) * (carrying ? 12f : 5f);
            rb.linearVelocity = Vector3.ClampMagnitude(velocity, carrying ? 10f : 2.5f);
            rb.angularVelocity *= 0.8f;
        }

        // A free place on the floor in front of the gnome, or the nearest free place around it.
        static Vector3 PickSetDownSpot(Job job, Vector3 body)
        {
            PhysGrabObject item = job.Item;
            float halfHeight = Mathf.Max(item.boundingBox.y, 0.1f) * 0.5f;
            float radius = Mathf.Max(item.boundingBox.x, item.boundingBox.z, 0.2f) * 0.5f + 0.05f;
            Vector3 forward = job.Player ? job.Player.transform.position - body : job.Gnome.enemy.Rigidbody.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 lift = Vector3.up * (halfHeight + 0.05f);

            Vector3 first = body + forward * 0.6f + lift;
            if (Clear(item, first, radius)) return first;
            for (int ring = 1; ring <= 3; ring++)
            {
                for (int step = 0; step < 8; step++)
                {
                    // alternate left and right of the direction to the player
                    float angle = (step / 2 + 1) * 45f * (step % 2 == 0 ? 1f : -1f);
                    Vector3 spot = body + Quaternion.Euler(0f, angle, 0f) * forward * (0.6f + ring * 0.5f) + lift;
                    if (job.Player && Vector3.Distance(spot, job.Player.transform.position) < 0.8f) continue;
                    if (Clear(item, spot, radius)) return spot;
                }
            }
            return first;
        }

        // True when no other loose object occupies the space around a point.
        static bool Clear(PhysGrabObject item, Vector3 point, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlaps, SemiFunc.LayerMaskGetPhysGrabObject(), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                PhysGrabObject other = overlaps[i].GetComponentInParent<PhysGrabObject>();
                if (!other || other == item) continue;
                if (other.GetComponent<EnemyRigidbody>()) continue;
                return false;
            }
            return true;
        }

        // While carried, an item passes through other loose objects so it cannot knock them about.
        static void PassThrough(PhysGrabObject item, bool on)
        {
            if (!item) return;
            if (on)
            {
                if (passing.ContainsKey(item)) return;
                var saved = new List<KeyValuePair<Collider, LayerMask>>();
                LayerMask loose = SemiFunc.LayerMaskGetPhysGrabObject();
                foreach (Collider collider in item.GetComponentsInChildren<Collider>())
                {
                    saved.Add(new KeyValuePair<Collider, LayerMask>(collider, collider.excludeLayers));
                    collider.excludeLayers |= loose;
                }
                passing[item] = saved;
                return;
            }
            if (!passing.TryGetValue(item, out List<KeyValuePair<Collider, LayerMask>> list)) return;
            foreach (KeyValuePair<Collider, LayerMask> pair in list)
            {
                if (pair.Key) pair.Key.excludeLayers = pair.Value;
            }
            passing.Remove(item);
        }

        static void Settle(float now)
        {
            for (int i = settling.Count - 1; i >= 0; i--)
            {
                Settling s = settling[i];
                if (s.Item && claimed.Contains(s.Item))
                {
                    settling.RemoveAt(i);
                    continue;
                }
                if (s.Item && s.Item.isActiveAndEnabled && now < s.Until)
                {
                    s.Item.OverrideIndestructible(0.5f);
                    float radius = Mathf.Max(s.Item.boundingBox.x, s.Item.boundingBox.y, s.Item.boundingBox.z, 0.2f) * 0.5f;
                    if (!Clear(s.Item, s.Item.centerPoint, radius)) continue;
                }
                if (s.Item) PassThrough(s.Item, false);
                settling.RemoveAt(i);
            }
        }

        // Gnomes never collide with something a gnome has fetched, so they cannot kick the pile about
        // and nothing in it can land on them.
        static void IgnoreGnomes(PhysGrabObject item)
        {
            Collider[] itemColliders = item.GetComponentsInChildren<Collider>();
            foreach (EnemyGnome gnome in EnemyGnomeDirector.instance.gnomes)
            {
                if (!gnome || !gnome.enemy || !gnome.enemy.EnemyParent) continue;
                foreach (Collider a in gnome.enemy.EnemyParent.GetComponentsInChildren<Collider>(true))
                {
                    if (!a) continue;
                    foreach (Collider b in itemColliders)
                    {
                        if (b) Physics.IgnoreCollision(a, b, true);
                    }
                }
            }
        }

        // Friendly gnomes are not hurt by being carried, dropped or bumped by loot (which would also
        // damage the loot). Weapons still work on them.
        static void MakeSturdy()
        {
            foreach (EnemyGnome gnome in EnemyGnomeDirector.instance.gnomes)
            {
                if (!gnome || !gnome.enemy || !gnome.enemy.HasHealth || !gnome.enemy.Health) continue;
                gnome.enemy.Health.impactHurt = false;
                gnome.enemy.Health.objectHurt = false;
            }
        }

        static void Release(Job job)
        {
            if (job.Item)
            {
                claimed.Remove(job.Item);
                if (job.Phase != Phase.ToItem)
                {
                    job.Item.OverrideIndestructible(1.5f);
                    if (job.Item.rb)
                    {
                        job.Item.rb.linearVelocity = Vector3.zero;
                        job.Item.rb.angularVelocity = Vector3.zero;
                    }
                    settling.Add(new Settling { Item = job.Item, Until = Time.time + SettleSeconds });
                }
            }
        }

        static bool InCart(PhysGrabObject item)
        {
            foreach (PhysGrabCart cart in carts)
            {
                if (cart && cart.itemsInCart != null && cart.itemsInCart.Contains(item)) return true;
            }
            return false;
        }

        static bool Alive(PlayerAvatar player)
        {
            return player && player.gameObject.activeInHierarchy && !player.isDisabled && !player.deadSet;
        }

        static bool AnyPlayerAlive(List<PlayerAvatar> players)
        {
            if (players == null) return false;
            foreach (PlayerAvatar player in players)
            {
                if (Alive(player)) return true;
            }
            return false;
        }

        static bool NearAnyPlayer(List<PlayerAvatar> players, Vector3 point, float distance)
        {
            foreach (PlayerAvatar player in players)
            {
                if (Alive(player) && Vector3.Distance(player.transform.position, point) <= distance) return true;
            }
            return false;
        }

        static PlayerAvatar NearestPlayer(Vector3 point)
        {
            PlayerAvatar best = null;
            float bestDistance = float.MaxValue;
            List<PlayerAvatar> players = SemiFunc.PlayerGetAll();
            if (players == null) return null;
            foreach (PlayerAvatar player in players)
            {
                if (!Alive(player)) continue;
                float distance = Vector3.Distance(player.transform.position, point);
                if (distance < bestDistance)
                {
                    best = player;
                    bestDistance = distance;
                }
            }
            return best;
        }

        static string Name(EnemyGnome gnome) => gnome ? $"Gnome {gnome.directorIndex}" : "Gnome";

        static void Say(string message)
        {
            if (UsefulGnomesPlugin.Verbose.Value) UsefulGnomesPlugin.Logger.LogInfo(message);
        }
    }
}
