using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace InfiniteEverything
{
    /// <summary>
    /// Models of the items the mod creates (temporary rounds, borrowed magazines and the rounds in them). The game only
    /// loads the bundles of what is in the raid (in the hideout: of what is in the profile). Showing an item whose bundle
    /// is missing throws "... .bundle is not loaded. You should load it first." in ObjectsFactory.CreateItem. In
    /// ReloadCylinderMagOperation.SetCamoraIndexForLoadAmmo that happens before the round is counted, so a revolver
    /// reload with temporary rounds looped forever (seen in game in the 2.5.0 test, errors.log).
    /// Before such an item is used, its bundles are loaded the way the game does it for a bot's gear
    /// (ObjectsFactory.LoadBundlesAndCreatePools); the caller starts its reload again when they are there.
    /// </summary>
    internal static class ItemModels
    {
        private sealed class Pending
        {
            public string Key;
            public List<ResourceKey> Keys;
            public Task Task;
            public Action Then;
            public float Deadline;
        }

        private static readonly List<Pending> Queue = new List<Pending>();

        /// <summary>
        /// Bundle paths loaded through here in this raid / hideout visit. They count as ready whatever the pools say,
        /// so a caller that starts its reload again after the load can never ask for the same load twice.
        /// </summary>
        private static readonly HashSet<string> Loaded = new HashSet<string>();

        internal static bool Ready(Item item)
        {
            return Ready(Keys(item));
        }

        internal static bool Ready(string tpl)
        {
            return Ready(Keys(tpl));
        }

        /// <summary>Loads the item's bundles, then runs <paramref name="then"/> (may be null) from Update.</summary>
        internal static void Load(Item item, Action then)
        {
            Load(Keys(item), then);
        }

        internal static void Load(string tpl, Action then)
        {
            Load(Keys(tpl), then);
        }

        private static List<ResourceKey> Keys(Item item)
        {
            var keys = new List<ResourceKey>();
            if (item == null)
            {
                return keys;
            }

            keys.AddRange(item.Template.AllResources);
            if (item is Magazine magazine)
            {
                foreach (Item round in magazine.Cartridges.Items)
                {
                    keys.AddRange(round.Template.AllResources); // the rounds visible in the magazine
                }
            }

            return keys;
        }

        private static List<ResourceKey> Keys(string tpl)
        {
            var keys = new List<ResourceKey>();
            if (!string.IsNullOrEmpty(tpl) && Singleton<ItemFactory>.Instantiated
                && Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(tpl, out ItemTemplate template))
            {
                keys.AddRange(template.AllResources);
            }

            return keys;
        }

        /// <summary>
        /// Same two places ObjectsFactory.LoadBundlesAndCreatePools looks at: a filled pool (what Pools.PopOrCreate
        /// needs) or a loaded bundle without a pool. True when there is no pool set at all (nothing can be loaded).
        /// </summary>
        private static bool Ready(List<ResourceKey> keys)
        {
            if (keys.Count == 0 || !Singleton<ObjectsFactory>.Instantiated)
            {
                return true;
            }

            ObjectsFactory factory = Singleton<ObjectsFactory>.Instance;
            ObjectsFactory.Pools pools = factory.GetPools(ObjectsFactory.PoolsCategory.Raid);
            if (pools == null)
            {
                return true;
            }

            foreach (ResourceKey key in keys)
            {
                if (Loaded.Contains(key.path))
                {
                    continue;
                }

                if (pools.PoolsDictionary.TryGetValue(key, out var pool))
                {
                    var filling = pool?.Source?.Task;
                    if (filling == null || !filling.IsCompleted || filling.IsFaulted || filling.IsCanceled)
                    {
                        return false;
                    }
                }
                else if (!factory._loadedBundlesPaths.ContainsValue(key.path))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Load(List<ResourceKey> keys, Action then)
        {
            if (keys.Count == 0)
            {
                return;
            }

            string name = keys[0].path;
            foreach (Pending waiting in Queue)
            {
                if (waiting.Key == name)
                {
                    waiting.Then = then ?? waiting.Then; // R pressed again while it loads: one load, the latest request
                    return;
                }
            }

            try
            {
                Task task = Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local, keys, JobYieldPriority.Immediate,
                    null, ObjectsFactory.DefaultCancellationToken);
                Queue.Add(new Pending { Key = name, Keys = keys, Task = task, Then = then, Deadline = Time.unscaledTime + 20f });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not load the model {name}: {ex}");
            }
        }

        /// <summary>Runs what was waiting for a model. Called every frame.</summary>
        internal static void Tick()
        {
            if (Queue.Count == 0 && Loaded.Count == 0)
            {
                return;
            }

            if (MainPlayer.Get() == null)
            {
                Queue.Clear();
                Loaded.Clear(); // the game unloads its bundles with the world (ObjectsFactory.UnloadBundles)
                return;
            }

            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Pending waiting = Queue[i];
                if (!waiting.Task.IsCompleted)
                {
                    if (Time.unscaledTime > waiting.Deadline)
                    {
                        Queue.RemoveAt(i);
                        Plugin.Log.LogWarning($"The model {waiting.Key} did not load in time; that reload was dropped.");
                    }

                    continue;
                }

                Queue.RemoveAt(i);
                if (waiting.Task.IsFaulted || waiting.Task.IsCanceled)
                {
                    Plugin.Log.LogError($"The model {waiting.Key} could not be loaded: {waiting.Task.Exception?.GetBaseException().Message}");
                    continue;
                }

                foreach (ResourceKey key in waiting.Keys)
                {
                    Loaded.Add(key.path);
                }

                try
                {
                    waiting.Then?.Invoke();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"After loading the model {waiting.Key}: {ex}");
                }
            }
        }
    }
}
