using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using GamePlay.Effects;
using GamePlay.Items;
using GamePlay.Weapons;
using Pools;
using UnityEngine;
using UnityEngine.Serialization;

namespace GamePlay.Characters
{
    public class CharacterUnit : PoolEntity, IHitable
    {
        public static int CharacterCount = 0;

        [Header("Components References (Playable)")]
        [SerializeField] private List<MonoBehaviour> components = new List<MonoBehaviour>();

        private CapabilityPack _pack;
        private CapabilityFlags _activeFlags;

        [Header("Weapons")]
        [SerializeField] protected Transform weaponHolder;
        [SerializeField] private Transform projectilePoint;
        [SerializeField] private Transform bodyscalable;

        [Header("Effects")]
        [FormerlySerializedAs("effectCaster")]
        [SerializeField] protected EffectComponent effectComponent;

        [Header("Sound Effects")]
        [SerializeField] private AudioClipName attackSfx = AudioClipName.SFX_CharacterAttack;
        [SerializeField, Min(1)] private int maxAttackSfxPerFrame = 6;
        [SerializeField] private bool playAttackVfxOnObstacleHit = false;
        [SerializeField, Min(1)] private int maxAttackVfxPerFrame = 3;
        [SerializeField, Min(0f)] private float obstacleAttackDespawnDelay = 0.5f;
        [SerializeField, Min(0f)] private float enemyAttackDespawnDelay = 0.5f;

        [Header("Character Config (Playable Override)")]
        [Tooltip("Nếu set, CharacterUnit sẽ dùng list này thay vì ConfigHolder era.")]
        [SerializeField] private CharacterListDataSO overrideCharacterList;
        [SerializeField] private float moveSpeed = 15f;

        [Header("Hit Settings")]
        [SerializeField] private ShapeType hitShapeType = ShapeType.Cylinder;
        [SerializeField] private Vector3 hitColliderSize = new Vector3(0.6f, 1.2f, 0.6f);

        [SerializeField] public int Level = -1;

        private GameObject _currentWeapon;
        private Renderer[] _currentWeaponRenderers;
        private CharacterListDataSO.CharacterEntry _characterData;
        private CharacterListDataSO _resolvedCharacterList;
        private bool _projectileTargetRegistered;
        private bool _isCountedInRuntime;
        public event Action<IAttacker> OnHitComplete;

        // jump properties
        private readonly IHitable[] _hitBuffer = new IHitable[5];
        private int _hitCount;

        private bool _isCombatActive = false;

        [Header("Death VFX")]
        [SerializeField] private GameObject dieVfxPrefab;
        [SerializeField] private Vector3 dieVfxOffset = Vector3.zero;
        [SerializeField] private float dieVfxLifetime = 1.2f;
        [SerializeField] private int maxDeathVfxPerFrame = 10;
        [SerializeField] private bool playDeathVfxOnAttackDespawn = false;
        [SerializeField] private Renderer _mainRenderer;
        private static int s_lastDeathVfxFrame = -1;
        private static int s_deathVfxCountInFrame = 0;
        private static int s_lastAttackSfxFrame = -1;
        private static int s_attackSfxCountInFrame = 0;
        private static int s_lastAttackVfxFrame = -1;
        private static int s_attackVfxCountInFrame = 0;
        private Coroutine _attackDespawnRoutine;
        private bool _isAttackDespawnScheduled;
        private static readonly Dictionary<int, VFX_SyncParticleColor> s_vfxColorSyncCache = new Dictionary<int, VFX_SyncParticleColor>(256);
        private static readonly Dictionary<int, ParticleSystem[]> s_vfxParticlesCache = new Dictionary<int, ParticleSystem[]>(256);
        private static readonly Dictionary<int, TimedAutoDisable> s_timedAutoDisableCache = new Dictionary<int, TimedAutoDisable>(256);
        private static readonly Dictionary<int, Color> s_materialColorCache = new Dictionary<int, Color>(128);

        public Transform ProjectilePoint => EnsureProjectilePoint();

        private static GameObject SafePoolGet(GameObject prefab)
        {
            if (prefab == null) return null;
            if (PoolManager.Instance == null) return Instantiate(prefab);
            try
            {
                var obj = PoolManager.Instance.Get(prefab);
                return obj != null ? obj : Instantiate(prefab);
            }
            catch
            {
                return Instantiate(prefab);
            }
        }

        private static void SafePoolRelease(GameObject obj)
        {
            if (obj == null) return;
            if (PoolManager.Instance != null)
            {
                try { obj.SetActive(false); return; } catch { }
            }
            Destroy(obj);
        }

        protected override void Awake()
        {
            base.Awake();

            BuildCapabilityPackOnce();
            Level = -1;

            if (weaponHolder == null)
                weaponHolder = FindChildByNameContains(transform, "Vu_khi");

            EnsureProjectilePoint();
            EnsureBodyScalable();

            gameObject.layer = 0;

            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            if (TryGetComponent<Collider>(out var col)) Destroy(col);

            if (_entityType == EntityType.None) _entityType = EntityType.Character;

            CacheMainRendererIfNeeded();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (weaponHolder == null)
                weaponHolder = FindChildByNameContains(transform, "Vu_khi");

            EnsureProjectilePoint();
            EnsureBodyScalable();

            CacheMainRendererIfNeeded();

            if (components == null) components = new List<MonoBehaviour>();
            if (components.Count == 0)
            {
                var monos = GetComponents<MonoBehaviour>();
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null) continue;
                    if (mb == this) continue;
                    if (mb is IComponent) components.Add(mb);
                }
            }
        }
#endif

        private void BuildCapabilityPackOnce()
        {
            _pack = default;
            _activeFlags = CapabilityFlags.None;

            if (components == null) components = new List<MonoBehaviour>();

            bool hasValidComponents = false;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null) { hasValidComponents = true; break; }
            }

            if (!hasValidComponents)
            {
                components.Clear();

                var monos = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < monos.Length; i++)
                {
                    var mb = monos[i];
                    if (mb == null || mb == this) continue;
                    if (mb is IComponent) components.Add(mb);
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                var mb = components[i];
                if (mb == null) continue;

                if (!(mb is IComponent val)) continue;

                if (val is IMover mover) { _pack.Mover = mover; _activeFlags |= CapabilityFlags.Move; }
                if (val is IAttacker attacker) { _pack.Attacker = attacker; _activeFlags |= CapabilityFlags.Attack; }
                if (val is IJumper jumper) { _pack.Jumper = jumper; _activeFlags |= CapabilityFlags.Jump; }
                if (val is IAnimator animator) { _pack.Animator = animator; _activeFlags |= CapabilityFlags.Animator; }
                if (val is IHealable healable) { _pack.Healable = healable; _activeFlags |= CapabilityFlags.Heal; }
            }

            if (_pack.Attacker == null)
            {
                var attackers = GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < attackers.Length; i++)
                {
                    if (attackers[i] is IAttacker attacker)
                    {
                        _pack.Attacker = attacker;
                        _activeFlags |= CapabilityFlags.Attack;
                        break;
                    }
                }
            }

            ValidateCapabilityPackState();
        }

        private void ValidateCapabilityPackState()
        {
            if (!IsCapabilityActive(_pack.Mover))
            {
                _pack.Mover = null;
                _activeFlags &= ~CapabilityFlags.Move;
            }

            if (!IsCapabilityActive(_pack.Attacker))
            {
                _pack.Attacker = null;
                _activeFlags &= ~CapabilityFlags.Attack;
            }

            if (!IsCapabilityActive(_pack.Jumper))
            {
                _pack.Jumper = null;
                _activeFlags &= ~CapabilityFlags.Jump;
            }

            if (!IsCapabilityActive(_pack.Animator))
            {
                _pack.Animator = null;
                _activeFlags &= ~CapabilityFlags.Animator;
            }

            if (!IsCapabilityActive(_pack.Healable))
            {
                _pack.Healable = null;
                _activeFlags &= ~CapabilityFlags.Heal;
            }
        }

        private static bool IsCapabilityActive(IComponent component)
        {
            if (component == null)
                return false;

            if (component is Behaviour behaviour)
                return behaviour.isActiveAndEnabled;

            var transform = component.Transform;
            return transform != null && transform.gameObject.activeInHierarchy;
        }

        public virtual void Initialize()
        {
            // Empty base - for override
        }

        public void Initialize(int level, bool isPassive = false)
        {
            if (isPassive)
            {
                InitializePreview(level);
                return;
            }

            ResetTransientRuntimeState();
            Setup(level, includeWeapon: true);
            ShowWeapon();

            if ((_activeFlags & CapabilityFlags.Move) != 0) _pack.Mover.Initialize();
            if ((_activeFlags & CapabilityFlags.Attack) != 0) _pack.Attacker.Initialize();
            if ((_activeFlags & CapabilityFlags.Jump) != 0) _pack.Jumper.Initialize();
            if ((_activeFlags & CapabilityFlags.Animator) != 0) _pack.Animator.Initialize();
            if ((_activeFlags & CapabilityFlags.Heal) != 0) _pack.Healable.Initialize();

            RegisterEvents(true);

            if (!_isCountedInRuntime)
            {
                CharacterCount++;
                _isCountedInRuntime = true;
            }

            RegisterProjectileTarget();
            CombatSystem.Register(transform, _pack, _activeFlags);
        }

        public void InitializePreview(int level)
        {
            ResetTransientRuntimeState();
            Setup(level, includeWeapon: false);

            if ((_activeFlags & CapabilityFlags.Animator) != 0)
            {
                _pack.Animator.Initialize();
                _pack.Animator.PlayAnimation(AnimationType.Idle, 0f, null);
            }

            HideWeapon();
        }

        private void OnDisable()
        {
            ReleaseCurrentWeapon();

            if (_attackDespawnRoutine != null)
            {
                StopCoroutine(_attackDespawnRoutine);
                _attackDespawnRoutine = null;
            }
            _isAttackDespawnScheduled = false;

            RegisterEvents(false);
            UnregisterProjectileTarget();
            ClearHits();
            _isCombatActive = false;

            if (!_isCountedInRuntime) return;
            CharacterCount = Mathf.Max(0, CharacterCount - 1);
            _isCountedInRuntime = false;
        }

        public void Setup(int level, bool includeWeapon = true)
        {
            if (Level == level)
            {
                if (_characterData == null)
                    _characterData = GetCharacterData();

                CacheMainRendererIfNeeded();

                if (includeWeapon)
                {
                    if (_currentWeapon == null)
                        SetupModel(includeWeapon: true);
                }
                else if (_currentWeapon != null)
                {
                    ReleaseCurrentWeapon();
                }

                return;
            }

            Level = level;
            _characterData = GetCharacterData();
            CacheMainRendererIfNeeded();
            SetupComponents();
            SetupModel(includeWeapon);
        }

        private void SetupComponents()
        {
            if (_characterData == null) return;
            if ((_activeFlags & CapabilityFlags.Attack) != 0)
                _pack.Attacker.Setup(_characterData.UnitDamage);
        }

        private void SetupModel(bool includeWeapon = true)
        {
            if (!includeWeapon)
            {
                ReleaseCurrentWeapon();
                return;
            }

            SetupWeapon();
        }

        private void RegisterEvents(bool register)
        {
            if (register)
            {
                if (_pack.Mover != null) _pack.Mover.OnMovementComplete += HandleMovementComplete;
                if (_pack.Attacker != null) _pack.Attacker.OnAttackComplete += HandleAttackComplete;
                if (_pack.Jumper != null) _pack.Jumper.OnJumperComplete += HandleJumperComplete;
                if (_pack.Healable != null) _pack.Healable.OnHealthChange += HandleHealthChange;
            }
            else
            {
                if (_pack.Mover != null) _pack.Mover.OnMovementComplete -= HandleMovementComplete;
                if (_pack.Attacker != null) _pack.Attacker.OnAttackComplete -= HandleAttackComplete;
                if (_pack.Jumper != null) _pack.Jumper.OnJumperComplete -= HandleJumperComplete;
                if (_pack.Healable != null) _pack.Healable.OnHealthChange -= HandleHealthChange;
            }
        }

        private void HandleMovementComplete()
        {
            DespawnInterval(false);
        }

        private void HandleAttackComplete(IHitable target)
        {
            if (target != null && target.EntityType == GamePlay.Entities.EntityType.CapacityGate)
            {
                DespawnInterval(true);
                return;
            }

            bool isObstacle = target != null && IsNonEnemyTarget(target.EntityType);

            if (target != null && SoundManager.Instance != null && CanPlayAttackSfxThisFrame())
            {
                var sfx = attackSfx != AudioClipName.None ? attackSfx : AudioClipName.SFX_CharacterAttack;
                if (sfx != AudioClipName.None)
                    SoundManager.Instance.PlayOneShot(sfx);
            }

            if (isObstacle)
            {
                if (playAttackVfxOnObstacleHit && effectComponent != null && CanPlayAttackVfxThisFrame())
                    effectComponent.PlayEffect(EffectType.Attack);

                float despawnDelay = ResolveAttackDespawnDelay(obstacleAttackDespawnDelay);
                _pack.Animator?.PlayAnimation(AnimationType.Attack, 0f, null);
                ScheduleAttackDespawn(despawnDelay, playDeathVfxOnAttackDespawn);
                return;
            }

            if (effectComponent != null) effectComponent.PlayEffect(EffectType.Attack);

            _pack.Animator?.PlayAnimation(AnimationType.Attack, 0f, null);
            ScheduleAttackDespawn(ResolveAttackDespawnDelay(enemyAttackDespawnDelay), dieVfxPrefab != null);
        }

        private void HandleJumperComplete(IHitable target)
        {
            if (TryAddTarget(target))
            {
                PlayAnimation(AnimationType.Jump);
            }
        }

        protected virtual void HandleHealthChange(int current, int max)
        {
            if (current <= 0)
            {
                DespawnInterval();
            }
        }

        public void PlayAnimation(AnimationType animationType, float waitForAction = 0.5f, Action onComplete = null)
        {
            _pack.Animator?.PlayAnimation(animationType, waitForAction, onComplete);
        }

        public void PlayAttackEffect()
        {
            effectComponent?.PlayEffect(EffectType.Attack, transform.position, transform.rotation);
        }

        private CharacterListDataSO.CharacterEntry GetCharacterData()
        {
            var list = ResolveCharacterList();
            if (list == null)
            {
                Debug.LogWarning($"CharacterUnit: No CharacterListDataSO (overrideCharacterList is null and ConfigHolder has no era). Object: {name}");
                return null;
            }

            return list.GetCharacterByLevel(Level);
        }

        // -----------------------------------------------------------------------
        // [FIX] SetupWeapon — use SafePoolGet instead of PoolManager.Instance.Get
        // -----------------------------------------------------------------------
        private void SetupWeapon()
        {
            ReleaseCurrentWeapon();

            if (_characterData == null) return;
            if (_characterData.WeaponPrefab == null) return;
            if (weaponHolder == null) return;

            _currentWeapon = SafePoolGet(_characterData.WeaponPrefab);

            if (_currentWeapon == null) return;

            _currentWeapon.transform.SetParent(weaponHolder, false);
            _currentWeaponRenderers = _currentWeapon.GetComponentsInChildren<Renderer>(true);

            if (_currentWeapon.TryGetComponent<WeaponUnit>(out var weaponUnit))
            {
                weaponUnit.SetDefault();
            }
        }

        public void HideWeapon()
        {
            SetWeaponVisible(false);
        }

        public void ShowWeapon()
        {
            if (_currentWeapon == null && _characterData != null && _characterData.WeaponPrefab != null)
            {
                SetupWeapon();
            }

            SetWeaponVisible(true);
        }

        public void SetWeaponPrefabOverride(GameObject prefab)
        {
            if (!EnsureWeaponHolder())
            {
                return;
            }

            for (int i = weaponHolder.childCount - 1; i >= 0; i--)
            {
                var child = weaponHolder.GetChild(i);
                SafePoolRelease(child.gameObject);
            }

            _currentWeapon = null;
            _currentWeaponRenderers = null;

            if (prefab == null)
            {
                return;
            }

            _currentWeapon = SafePoolGet(prefab);

            if (_currentWeapon == null)
            {
                return;
            }

            var weaponTransform = _currentWeapon.transform;
            weaponTransform.SetParent(weaponHolder, false);
            weaponTransform.localPosition = Vector3.zero;
            weaponTransform.localRotation = Quaternion.identity;
            weaponTransform.localScale = Vector3.one;

            _currentWeaponRenderers = _currentWeapon.GetComponentsInChildren<Renderer>(true);
            if (_currentWeapon.TryGetComponent<WeaponUnit>(out var weaponUnit))
            {
                weaponUnit.SetDefault();
            }
        }

        private bool EnsureWeaponHolder()
        {
            if (weaponHolder != null)
            {
                return true;
            }

            weaponHolder = FindChildByNameContains(transform, "Vu_khi");
            return weaponHolder != null;
        }

        private Transform EnsureProjectilePoint()
        {
            if (projectilePoint != null)
            {
                return projectilePoint;
            }

            var projectilePoints = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < projectilePoints.Length; i++)
            {
                var candidate = projectilePoints[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.name, "ProjectilePoint", StringComparison.Ordinal))
                {
                    projectilePoint = candidate;
                    break;
                }
            }

            return projectilePoint;
        }

        private Transform EnsureBodyScalable()
        {
            if (bodyscalable != null)
            {
                return bodyscalable;
            }

            if (transform.childCount > 0)
            {
                bodyscalable = transform.GetChild(0);
            }

            return bodyscalable;
        }

        public WeaponUnit DetachWeaponForProjectile()
        {
            if (_currentWeapon == null)
            {
                return null;
            }

            var weapon = _currentWeapon;
            var weaponUnit = weapon.GetComponent<WeaponUnit>() != null
                ? weapon.GetComponent<WeaponUnit>()
                : weapon.GetComponentInChildren<WeaponUnit>(true);
            if (weaponUnit == null)
            {
                return null;
            }

            _currentWeapon = null;
            _currentWeaponRenderers = null;
            weapon.transform.SetParent(null, true);
            return weaponUnit;
        }

        private void DespawnInterval(bool playDeathVfx = true)
        {
            RecycleImmediate(playDeathVfx);
        }

        public void RecycleImmediate(bool playDeathVfx = false)
        {
            if (_attackDespawnRoutine != null)
            {
                StopCoroutine(_attackDespawnRoutine);
                _attackDespawnRoutine = null;
            }
            _isAttackDespawnScheduled = false;

            if ((_activeFlags & CapabilityFlags.Move) != 0) _pack.Mover.Dispose();
            if ((_activeFlags & CapabilityFlags.Attack) != 0) _pack.Attacker.Dispose();
            if ((_activeFlags & CapabilityFlags.Jump) != 0) _pack.Jumper.Dispose();
            if ((_activeFlags & CapabilityFlags.Animator) != 0) _pack.Animator.Dispose();
            if ((_activeFlags & CapabilityFlags.Heal) != 0) _pack.Healable.Dispose();

            RegisterEvents(false);
            UnregisterProjectileTarget();

            if (_isCountedInRuntime)
            {
                CharacterCount = Mathf.Max(0, CharacterCount - 1);
                _isCountedInRuntime = false;
            }

            ClearHits();
            _isCombatActive = false;

            if (playDeathVfx)
            {
                PlayDeathVfx();
            }

            Despawn();
        }

        public bool IsActive => isActiveAndEnabled;

        public Vector3 Position => Transform.position;

        public Vector3 SelfScale
        {
            get
            {
                var scaleTarget = EnsureBodyScalable();
                return scaleTarget != null ? scaleTarget.localScale : transform.localScale;
            }
        }

        public void OnHit(IAttacker source)
        {
            if (_isAttackDespawnScheduled)
                return;

            if (effectComponent != null)
            {
                effectComponent.PlayEffect(EffectType.Hit, transform.position, transform.rotation);
            }

            DespawnInterval();
            OnHitComplete?.Invoke(source);
        }

        private void ScheduleAttackDespawn(float delay, bool playDeathVfx)
        {
            if (_attackDespawnRoutine != null)
            {
                StopCoroutine(_attackDespawnRoutine);
                _attackDespawnRoutine = null;
            }

            float safeDelay = Mathf.Max(0f, delay);
            if (safeDelay <= 0f)
            {
                _isAttackDespawnScheduled = false;
                DespawnInterval(playDeathVfx);
                return;
            }

            _isAttackDespawnScheduled = true;
            _attackDespawnRoutine = StartCoroutine(CoAttackDespawnAfterDelay(safeDelay, playDeathVfx));
        }

        private void ResetTransientRuntimeState()
        {
            if (_attackDespawnRoutine != null)
            {
                StopCoroutine(_attackDespawnRoutine);
                _attackDespawnRoutine = null;
            }

            _isAttackDespawnScheduled = false;
            RegisterEvents(false);
            UnregisterProjectileTarget();
            ClearHits();
            _isCombatActive = false;
        }

        private IEnumerator CoAttackDespawnAfterDelay(float delay, bool playDeathVfx)
        {
            yield return new WaitForSeconds(delay);
            _attackDespawnRoutine = null;
            _isAttackDespawnScheduled = false;
            DespawnInterval(playDeathVfx);
        }

        private float ResolveAttackDespawnDelay(float configuredDelay)
        {
            float delay = Mathf.Max(0f, configuredDelay);
            if (!(_pack.Animator is AnimationComponent animationComponent))
                return delay;

            float attackClipLength = animationComponent.GetAnimationClipLength(AnimationType.Attack);
            if (attackClipLength <= 0f)
                return delay;

            return Mathf.Max(delay, attackClipLength);
        }

        // -----------------------------------------------------------------------
        // [FIX] PlayDeathVfx — use SafePoolGet instead of PoolManager.Instance.Get
        // -----------------------------------------------------------------------
        private void PlayDeathVfx()
        {
            if (dieVfxPrefab == null)
                return;
            if (!CanSpawnDeathVfxThisFrame())
                return;

            CacheMainRendererIfNeeded();

            var spawnPos = Transform.position + dieVfxOffset;
            var vfx = SafePoolGet(dieVfxPrefab);
            if (vfx == null) return;

            vfx.transform.position = spawnPos;
            vfx.transform.rotation = Quaternion.identity;
            vfx.SetActive(true);

            if (_mainRenderer != null)
            {
                var sync = GetCachedColorSync(vfx);
                if (sync != null)
                {
                    sync.SyncColorFrom(_mainRenderer);
                }
                else
                {
                    Color c = GetCachedRendererColor(_mainRenderer);

                    var parts = GetCachedParticleSystems(vfx);
                    if (parts != null)
                    {
                        for (int i = 0; i < parts.Length; i++)
                        {
                            var p = parts[i];
                            if (p == null) continue;
                            var main = p.main;
                            main.startColor = c;
                        }
                    }
                }
            }
            var autoDisable = GetOrAddTimedAutoDisable(vfx);
            if (autoDisable == null) return;
            autoDisable.Play(Mathf.Max(0.05f, dieVfxLifetime));
        }

        private static VFX_SyncParticleColor GetCachedColorSync(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_vfxColorSyncCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            vfxObject.TryGetComponent(out cached);
            s_vfxColorSyncCache[key] = cached;
            return cached;
        }

        private static ParticleSystem[] GetCachedParticleSystems(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_vfxParticlesCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            cached = vfxObject.GetComponentsInChildren<ParticleSystem>(true);
            s_vfxParticlesCache[key] = cached;
            return cached;
        }

        private static TimedAutoDisable GetOrAddTimedAutoDisable(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_timedAutoDisableCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (!vfxObject.TryGetComponent(out cached))
                cached = vfxObject.AddComponent<TimedAutoDisable>();

            s_timedAutoDisableCache[key] = cached;
            return cached;
        }

        private CharacterListDataSO ResolveCharacterList()
        {
            if (overrideCharacterList != null)
            {
                if (_resolvedCharacterList != overrideCharacterList)
                    _resolvedCharacterList = overrideCharacterList;

                return _resolvedCharacterList;
            }

            if (_resolvedCharacterList != null)
                return _resolvedCharacterList;

            EraDataSO era = null;
            if (ConfigHolder.Instance != null)
            {
                era = ConfigHolder.Instance.GetCurrentEraConfig();
                if (era != null && era.CharacterList != null)
                {
                    _resolvedCharacterList = era.CharacterList;
                    return _resolvedCharacterList;
                }
            }

            if (GameplayManager.Instance != null)
            {
                era = GameplayManager.Instance.PlayableEra;
                if (era != null && era.CharacterList != null)
                {
                    _resolvedCharacterList = era.CharacterList;
                    return _resolvedCharacterList;
                }
            }

            return null;
        }

        private void CacheMainRendererIfNeeded()
        {
            if (_mainRenderer != null)
                return;

            var renderers = GetComponentsInChildren<Renderer>(true);
            SkinnedMeshRenderer bestSkinned = null;
            MeshRenderer bestMesh = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (candidate == null)
                    continue;

                if (candidate is ParticleSystemRenderer)
                    continue;

                var candidateTransform = candidate.transform;
                if (weaponHolder != null && candidateTransform != null && candidateTransform.IsChildOf(weaponHolder))
                    continue;

                if (bestSkinned == null && candidate is SkinnedMeshRenderer skinned)
                {
                    bestSkinned = skinned;
                    continue;
                }

                if (bestMesh == null && candidate is MeshRenderer mesh)
                    bestMesh = mesh;
            }

            if (bestSkinned != null)
            {
                _mainRenderer = bestSkinned;
                return;
            }

            if (bestMesh != null)
            {
                _mainRenderer = bestMesh;
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (candidate == null || candidate is ParticleSystemRenderer)
                    continue;

                var candidateTransform = candidate.transform;
                if (weaponHolder != null && candidateTransform != null && candidateTransform.IsChildOf(weaponHolder))
                    continue;

                _mainRenderer = candidate;
                return;
            }
        }

        private static Color GetCachedRendererColor(Renderer renderer)
        {
            if (renderer == null)
                return Color.white;

            var material = renderer.sharedMaterial;
            if (material == null)
                return Color.white;

            int key = material.GetInstanceID();
            if (s_materialColorCache.TryGetValue(key, out var cached))
                return cached;

            Color resolved = Color.white;
            if (material.HasProperty("_BaseColor"))
                resolved = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                resolved = material.color;

            s_materialColorCache[key] = resolved;
            return resolved;
        }

        private bool CanSpawnDeathVfxThisFrame()
        {
            if (Time.frameCount != s_lastDeathVfxFrame)
            {
                s_lastDeathVfxFrame = Time.frameCount;
                s_deathVfxCountInFrame = 0;
            }

            int frameCap = Mathf.Max(1, maxDeathVfxPerFrame);
            if (s_deathVfxCountInFrame >= frameCap)
                return false;

            s_deathVfxCountInFrame++;
            return true;
        }

        private bool CanPlayAttackSfxThisFrame()
        {
            if (Time.frameCount != s_lastAttackSfxFrame)
            {
                s_lastAttackSfxFrame = Time.frameCount;
                s_attackSfxCountInFrame = 0;
            }

            int cap = Mathf.Max(1, maxAttackSfxPerFrame);
            if (s_attackSfxCountInFrame >= cap)
                return false;

            s_attackSfxCountInFrame++;
            return true;
        }

        private bool CanPlayAttackVfxThisFrame()
        {
            if (Time.frameCount != s_lastAttackVfxFrame)
            {
                s_lastAttackVfxFrame = Time.frameCount;
                s_attackVfxCountInFrame = 0;
            }

            int cap = Mathf.Max(1, maxAttackVfxPerFrame);
            if (s_attackVfxCountInFrame >= cap)
                return false;

            s_attackVfxCountInFrame++;
            return true;
        }

        private static bool IsNonEnemyTarget(GamePlay.Entities.EntityType entityType)
        {
            if (entityType == GamePlay.Entities.EntityType.Enemy ||
                entityType == GamePlay.Entities.EntityType.EnemyWeapon ||
                entityType == GamePlay.Entities.EntityType.PlayerWeapon ||
                entityType == GamePlay.Entities.EntityType.Character ||
                entityType == GamePlay.Entities.EntityType.Wheel)
            {
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            // No-op for IHitable/IComponent compatibility
        }

        public override void Free()
        {
            ReleaseCurrentWeapon();
            base.Free();
        }

        public ColliderData GetColliderData()
        {
            uint bits = 1u << (int)EntityType;

            if (hitShapeType == ShapeType.Sphere)
            {
                float r = Mathf.Max(0.01f, hitColliderSize.x);
                float centerOffsetY = Mathf.Max(0f, hitColliderSize.y);
                return new ColliderData
                {
                    Type = ShapeType.Sphere,
                    Size = new Vector3(r, centerOffsetY, r),
                    Offset = hitColliderSize.x,
                    CategoryBits = bits
                };
            }

            if (hitShapeType == ShapeType.Cylinder)
            {
                float r = Mathf.Max(0.01f, hitColliderSize.x);
                float halfH = Mathf.Max(0.01f, hitColliderSize.y * 0.5f);
                return new ColliderData
                {
                    Type = ShapeType.Cylinder,
                    Size = new Vector3(r, halfH, r),
                    Offset = hitColliderSize.x,
                    CategoryBits = bits
                };
            }

            Vector3 half = new Vector3(
                Mathf.Max(0.01f, hitColliderSize.x) * 0.5f,
                Mathf.Max(0.01f, hitColliderSize.y) * 0.5f,
                Mathf.Max(0.01f, hitColliderSize.z) * 0.5f);

            return new ColliderData
            {
                Type = ShapeType.Box,
                Size = half,
                Offset = hitColliderSize.z,
                CategoryBits = bits
            };
        }

        private void RegisterProjectileTarget()
        {
            if (_projectileTargetRegistered) return;
            EnemyProjectileSystem.RegisterTarget(this);
            _projectileTargetRegistered = true;
        }

        private void UnregisterProjectileTarget()
        {
            if (!_projectileTargetRegistered) return;
            EnemyProjectileSystem.UnregisterTarget(this);
            _projectileTargetRegistered = false;
        }

        private void ReleaseCurrentWeapon()
        {
            if (_currentWeapon == null) return;

            _currentWeapon.transform.SetParent(null, false);
            SafePoolRelease(_currentWeapon);

            _currentWeapon = null;
            _currentWeaponRenderers = null;
        }

        private void SetWeaponVisible(bool visible)
        {
            if (_currentWeapon == null) return;

            if (_currentWeaponRenderers == null || _currentWeaponRenderers.Length == 0)
                _currentWeaponRenderers = _currentWeapon.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < _currentWeaponRenderers.Length; i++)
            {
                var renderer = _currentWeaponRenderers[i];
                if (renderer == null) continue;
                renderer.enabled = visible;
            }
        }

        #region JUMP

        private bool TryAddTarget(IHitable target)
        {
            if (target == null) return false;

            for (int i = 0; i < _hitCount; i++)
            {
                if (_hitBuffer[i] == target) return false;
            }

            if (_hitCount < _hitBuffer.Length)
            {
                _hitBuffer[_hitCount] = target;
                _hitCount++;
                return true;
            }

            return false;
        }

        private void ClearHits()
        {
            for (int i = 0; i < _hitCount; i++)
                _hitBuffer[i] = null;

            _hitCount = 0;
        }

        #endregion

        private static Transform FindChildByNameContains(Transform root, string contains)
        {
            if (root == null || string.IsNullOrEmpty(contains)) return null;

            var stack = new Stack<Transform>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t != null && t.name != null && t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }

            return null;
        }
    }
}