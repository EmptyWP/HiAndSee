using UnityEngine;

namespace HiAndSee.Game
{
    public static class GroundSpawnArea
    {
        const int MaxAttempts = 24;

        public static bool TryGetRandomPoint(
            string groundName,
            float yOffset,
            float edgePadding,
            out Vector3 point)
        {
            point = default;
            if (!TryGetGroundBounds(groundName, out var ground, out var bounds))
                return false;

            float minX = bounds.min.x + edgePadding;
            float maxX = bounds.max.x - edgePadding;
            float minZ = bounds.min.z + edgePadding;
            float maxZ = bounds.max.z - edgePadding;

            if (minX > maxX)
            {
                minX = bounds.min.x;
                maxX = bounds.max.x;
            }

            if (minZ > maxZ)
            {
                minZ = bounds.min.z;
                maxZ = bounds.max.z;
            }

            for (int i = 0; i < MaxAttempts; i++)
            {
                float x = Random.Range(minX, maxX);
                float z = Random.Range(minZ, maxZ);
                point = new Vector3(x, bounds.max.y + yOffset, z);
                var rayStart = new Vector3(x, bounds.max.y + 40f, z);

                if (Physics.Raycast(rayStart, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform == ground || hit.collider.transform.IsChildOf(ground) || IsGroundLike(hit.collider.name))
                    {
                        point = hit.point + Vector3.up * yOffset;
                        return true;
                    }
                }
            }
            return true;
        }

        static bool TryGetGroundBounds(string groundName, out Transform ground, out Bounds bounds)
        {
            ground = FindGround(groundName);
            bounds = default;
            if (ground == null) return false;

            bool hasBounds = false;

            foreach (var renderer in ground.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (var collider in ground.GetComponentsInChildren<Collider>(true))
            {
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        static Transform FindGround(string groundName)
        {
            if (!string.IsNullOrWhiteSpace(groundName))
            {
                var exact = GameObject.Find(groundName);
                if (exact != null) return exact.transform;
            }

            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                string name = transform.name;
                if (!string.IsNullOrWhiteSpace(groundName) && name.Contains(groundName))
                    return transform;

                if (IsGroundLike(name))
                    return transform;
            }

            return null;
        }

        static bool IsGroundLike(string name)
        {
            return name.Contains("Ground_Mesh") || name.Contains("Ground") || name.Contains("Floor");
        }
    }
}
