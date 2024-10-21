using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;

namespace ValheimPlus.Utility
{
    static class GameObjectAssistant
    {
        // TODO memory leak
        private static readonly ConcurrentDictionary<float, Stopwatch> Stopwatches = new();

        public static Stopwatch GetStopwatch(GameObject o)
        {
            var hash = GetGameObjectPositionHash(o);
            if (Stopwatches.TryGetValue(hash, out var stopwatch)) return stopwatch;
            
            stopwatch = new Stopwatch();
            Stopwatches.TryAdd(hash, stopwatch);
            return stopwatch;
        }

        public static float GetGameObjectPositionHash(GameObject obj)
        {
            var position = obj.transform.position;
            return 1000f * position.x + position.y + .001f * position.z;
        }

        public static T GetChildComponentByName<T>(string name, GameObject objected) where T : Component
        {
            foreach (var component in objected.GetComponentsInChildren<T>(true))
            {
                if (component.gameObject.name == name)
                {
                    return component;
                }
            }
            return null;
        }
    }
}
