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

        private MaterialPropertyBlock _propBlock;
        private bool _simulating;
        private Vector3 _currentVelocity;
        private Vector3 _currentPosition;
        private float _elapsedTime;

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
            _elapsedTime = 0f;

            _angularVelocity = new Vector3(
                Random.Range(-360f, 360f),
                Random.Range(-360f, 360f),
                Random.Range(-360f, 360f)
            );

            _currentVelocity = _initialVelocity;
            _currentPosition = _startPosition;
            _simulating = true;
            enabled = true;
        }

        private bool Step(float dt)
        {
            if (dt <= 0f)
            {
                dt = Time.unscaledDeltaTime;
            }

            _elapsedTime += dt;

            _currentVelocity.y -= _gravity * dt;
            _currentPosition += _currentVelocity * dt;

            bool isWithinXRange = _currentPosition.x >= -7f && _currentPosition.x <= 7f;
            if (_currentPosition.y <= 0f && isWithinXRange)
            {
                if (!_hasBounced)
                {
                    _currentVelocity.y = Mathf.Abs(_currentVelocity.y) * _bounceMultiplier;
                    _currentVelocity.x *= _bounceMultiplier;
                    _currentVelocity.z *= _bounceMultiplier;
                    _hasBounced = true;
                    _currentPosition.y = 0f;
                }
                else
                {
                    _currentVelocity = Vector3.zero;
                    _currentPosition.y = 0f;
                }
            }

            transform.position = _currentPosition;
            transform.Rotate(_angularVelocity * dt, Space.Self);

            if (_elapsedTime < _lifetime)
            {
                return true;
            }

            _simulating = false;
            gameObject.SetActive(false);
            return false;
        }

        private void Update()
        {
            if (!_simulating) return;
            Step(Time.unscaledDeltaTime);
        }

        private void StopSimulation()
        {
            _simulating = false;
            enabled = false;
        }

        private void OnDisable()
        {
            StopSimulation();
        }

        private void OnDestroy()
        {
            StopSimulation();
        }
    }
}
