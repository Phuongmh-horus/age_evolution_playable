using UnityEngine;
using Random = UnityEngine.Random;

namespace GamePlay.Effects
{
    public class DebrisBlock : MonoBehaviour
    {
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        public MeshRenderer meshRenderer;

        private Vector3 _initialVelocity;
        private Vector3 _angularVelocity;
        private float _gravity = 20f;
        private float _bounceMultiplier = 0.5f;
        private float _lifetime;

        private Vector3 _startPosition;
        private bool _hasBounced;

        private Coroutine _routine;
        private MaterialPropertyBlock _propBlock;

        public void SetColor(Color color)
        {
            if (meshRenderer == null) return;

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(ColorProp, color);
            meshRenderer.SetPropertyBlock(_propBlock);
        }

        public void Initialize(Vector3 initialVelocity, float lifetime)
        {
            _initialVelocity = initialVelocity;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _hasBounced = false;
            _startPosition = transform.position;

            StopRoutine();

            _angularVelocity = new Vector3(
                Random.Range(-360f, 360f),
                Random.Range(-360f, 360f),
                Random.Range(-360f, 360f)
            );

            _routine = StartCoroutine(PhysicsRoutine());
        }

        private System.Collections.IEnumerator PhysicsRoutine()
        {
            float elapsedTime = 0f;
            Vector3 currentVelocity = _initialVelocity;
            Vector3 currentPosition = _startPosition;

            while (elapsedTime < _lifetime)
            {
                float dt = Time.deltaTime;
                elapsedTime += dt;

                currentVelocity.y -= _gravity * dt;
                currentPosition += currentVelocity * dt;

                // ground collision only when x in [-7, 7] (giữ logic gốc)
                bool isWithinXRange = currentPosition.x >= -7f && currentPosition.x <= 7f;

                if (currentPosition.y <= 0f && isWithinXRange)
                {
                    if (!_hasBounced)
                    {
                        currentVelocity.y = Mathf.Abs(currentVelocity.y) * _bounceMultiplier;
                        currentVelocity.x *= _bounceMultiplier;
                        currentVelocity.z *= _bounceMultiplier;
                        _hasBounced = true;
                        currentPosition.y = 0f;
                    }
                    else
                    {
                        currentVelocity = Vector3.zero;
                        currentPosition.y = 0f;
                    }
                }

                transform.position = currentPosition;
                transform.Rotate(_angularVelocity * dt, Space.Self);

                yield return null;
            }

            _routine = null;
            gameObject.SetActive(false);
        }

        private void StopRoutine()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private void OnDisable()
        {
            StopRoutine();
        }

        private void OnDestroy()
        {
            StopRoutine();
        }
    }
}
