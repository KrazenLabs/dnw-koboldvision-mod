using HarmonyLib;
using UnityEngine;

namespace KoboldVision
{
    internal static class Washing
    {
        private static readonly AccessTools.FieldRef<WalkNWashSceneState, bool> IsSexScene = AccessTools.FieldRefAccess<WalkNWashSceneState, bool>("isSexScene");

        private static WalkNWashSceneState _scene;

        public static SkinnedMeshRenderer FindDragonBody()
        {
            if (!WalkNWashSceneState.TryGetActiveDragon(out var dragon) || dragon.skin == null) return null;
            if (_scene == null) _scene = Object.FindFirstObjectByType<WalkNWashSceneState>();
            if (_scene == null || IsSexScene(_scene)) return null;
            return dragon.skin;
        }
    }
}
