using System.Collections;
using UnityEngine;

namespace GamePlay.Effects
{
    public class TimedAutoDisable : MonoBehaviour
    {
        private Coroutine _disableRoutine;
        private ParticleSystem[] _particleSystems;

        private void Awake()
        {
            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        public void Play(float lifeTime)
        {
            if (_disableRoutine != null)
                StopCoroutine(_disableRoutine);

            if (_particleSystems == null || _particleSystems.Length == 0)
                _particleSystems = GetComponentsInChildren<ParticleSystem>(true);

            for (int i = 0; i < _particleSystems.Length; i++)
            {
                var ps = _particleSystems[i];
                if (ps == null) continue;
                ps.Clear();
                ps.Play(true);
            }

            _disableRoutine = StartCoroutine(CoDisableAfter(Mathf.Max(0.05f, lifeTime)));
        }

        private IEnumerator CoDisableAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            _disableRoutine = null;
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (_disableRoutine != null)
            {
                StopCoroutine(_disableRoutine);
                _disableRoutine = null;
            }
        }
    }
}
