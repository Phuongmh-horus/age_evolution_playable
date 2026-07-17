using UnityEngine;

namespace Pools
{
    /// <summary>
    /// Wrapper automatically added to GameObjects/Components spawned via PoolSystem
    /// that do not implement IPoolable themselves.
    /// </summary>
    public class GenericPoolable : MonoBehaviour, IPoolable
    {
        public void New()
        {
            // SetActive is handled by PoolSystem.Spawn
        }

        public void Free()
        {
            // SetActive is handled by PoolSystem.Despawn
        }
    }
}
