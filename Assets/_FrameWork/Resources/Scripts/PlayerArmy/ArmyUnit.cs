using UnityEngine;

namespace PlayerArmy
{
    [DisallowMultipleComponent]
    public class ArmyUnit : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string moveState = "Move";
        [SerializeField] private string attackState = "Attack";
        [SerializeField] private string hitTrigger = "Hit";

        [Header("Weapon")]
        [SerializeField] private Transform weaponHand;
        [SerializeField] private Transform spawnBulletPosition;
        [SerializeField] private GameObject currentWeapon;

        public Animator Animator => animator;
        public Transform SpawnBulletPosition => spawnBulletPosition;
        public Transform WeaponHand => weaponHand;
        public GameObject CurrentWeapon => currentWeapon;

        public void Initialize(PlayerArmySystem owner = null)
        {
            CacheReferences();
            if (owner != null)
            {
                transform.SetParent(owner.transform, true);
            }
        }

        public void PlayIdle()
        {
            PlayState(idleState);
        }

        public void PlayMove()
        {
            PlayState(moveState);
        }

        public void PlayAttack()
        {
            PlayState(attackState);
        }

        public void PlayHit()
        {
            PlayTrigger(hitTrigger);
        }

        public void PlayTrigger(string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                return;
            }

            animator.SetTrigger(triggerName);
        }

        public void PlayState(string stateName, float transitionDuration = 0f)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            animator.CrossFadeInFixedTime(stateName, transitionDuration);
        }

        public void SetWeapon(GameObject weaponPrefab)
        {
            ClearWeapon();

            if (weaponPrefab == null || weaponHand == null)
            {
                return;
            }

            currentWeapon = Instantiate(weaponPrefab, weaponHand, false);
            currentWeapon.transform.localPosition = Vector3.zero;
            currentWeapon.transform.localRotation = Quaternion.identity;
            currentWeapon.transform.localScale = Vector3.one;
        }

        public void ClearWeapon()
        {
            if (currentWeapon == null)
            {
                return;
            }

            Destroy(currentWeapon);
            currentWeapon = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheReferences();
        }
#endif

        private void Awake()
        {
            CacheReferences();
        }

        private void CacheReferences()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (weaponHand == null)
            {
                weaponHand = FindChildContains(transform, "WeaponHand");
            }

            if (spawnBulletPosition == null)
            {
                spawnBulletPosition = FindChildContains(transform, "SpawnBulletPosition");
            }
        }

        private static Transform FindChildContains(Transform root, string contains)
        {
            if (root == null || string.IsNullOrEmpty(contains))
            {
                return null;
            }

            var queue = new System.Collections.Generic.Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != null && current.name.Contains(contains))
                {
                    return current;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }

            return null;
        }
    }
}
