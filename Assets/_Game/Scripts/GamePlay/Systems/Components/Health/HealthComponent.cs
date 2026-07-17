using System;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.HealthSystems
{
    /// <summary>
    /// Health component tối giản cho playable (không phụ thuộc Alchemy/KBCore).
    /// Đủ để:
    /// - Combat/HitTextFlyEffect subscribe OnTakeDamaged
    /// - Gameplay flow subscribe OnDead (CashTower / Enemy...)
    /// - Là một IComponent trong component system.
    /// </summary>
    public class HealthComponent : MonoBehaviour, IComponent, IHealable
    {
        [SerializeField] private int maxHealth = 10;
        [SerializeField] private int currentHealth = 10;
        [SerializeField] private bool isImmortal = false;

        /// <summary>Gọi khi nhận damage (tham số: damage).</summary>
        public event Action<int> OnTakeDamaged;

        /// <summary>Gọi khi chết (currentHealth về 0).</summary>
        public event Action OnDead;

        /// <summary>Gọi khi máu thay đổi (current, max).</summary>
        public event Action<int, int> OnHealthChanged;

        // IHealable event
        public event Action<int, int> OnHealthChange;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0;
        public bool IsImmortal => isImmortal;

        #region IComponent

        public Transform Transform => transform;
        public bool IsEnabled => isActiveAndEnabled;

        /// <summary>
        /// IComponent.Initialize: đảm bảo máu hợp lệ.
        /// </summary>
        public void Initialize()
        {
            int normalizedMax = Mathf.Max(1, maxHealth);
            int normalizedCurrent = Mathf.Clamp(currentHealth, isImmortal ? 1 : 0, normalizedMax);
            bool changed = normalizedMax != maxHealth || normalizedCurrent != currentHealth;

            maxHealth = normalizedMax;
            currentHealth = normalizedCurrent;

            if (changed)
                NotifyHealthChanged();
        }

        public void OnUpdate(float dt)
        {
            // Không cần update theo frame trong playable mặc định.
        }

        public void Dispose()
        {
            // Không giữ resource unmanaged.
        }

        #endregion

        #region IHealable Implementation

        public void TakeDamage(IAttacker source)
        {
            if (source == null) return;
            TakeDamage(source.Damage);
        }

        public int GetCurrentHealth() => currentHealth;
        public int GetMaxHealth() => maxHealth;

        #endregion

        #region Public API

        public void SetMaxHealth(int value, bool refill = true)
        {
            maxHealth = Mathf.Max(1, value);
            if (refill) currentHealth = maxHealth;
            else currentHealth = Mathf.Clamp(currentHealth, isImmortal ? 1 : 0, maxHealth);

            NotifyHealthChanged();
        }

        public void SetHealth(int value)
        {
            int minValue = isImmortal ? 1 : 0;
            int newValue = Mathf.Clamp(value, minValue, maxHealth);
            if (newValue == currentHealth) return;

            currentHealth = newValue;
            NotifyHealthChanged();

            if (currentHealth <= 0 && !isImmortal)
                OnDead?.Invoke();
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || IsDead) return;
            SetHealth(currentHealth + amount);
        }

        public void TakeDamage(int amount)
        {
            ApplyDamageInternal(amount, notifyDamageEvent: true);
        }

        public void TakeDamageSilently(int amount)
        {
            ApplyDamageInternal(amount, notifyDamageEvent: false);
        }

        public void SetImmortal(bool value, bool clampCurrent = true)
        {
            if (isImmortal == value) return;
            isImmortal = value;

            if (clampCurrent)
            {
                currentHealth = Mathf.Clamp(currentHealth, isImmortal ? 1 : 0, maxHealth);
                NotifyHealthChanged();
            }
        }

        private void NotifyHealthChanged()
        {
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnHealthChange?.Invoke(currentHealth, maxHealth);
        }

        private void ApplyDamageInternal(int amount, bool notifyDamageEvent)
        {
            if (amount <= 0 || IsDead) return;

            if (notifyDamageEvent)
            {
                // Fire event even if Immortal to show FlyText/Feedback
                OnTakeDamaged?.Invoke(amount);
            }

            if (isImmortal) return;

            currentHealth = Mathf.Clamp(currentHealth - amount, 0, maxHealth);

            NotifyHealthChanged();

            if (currentHealth <= 0)
                OnDead?.Invoke();
        }

        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, isImmortal ? 1 : 0, maxHealth);
        }
#endif
    }
}
