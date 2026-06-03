using UnityEngine;
using Random = UnityEngine.Random;

namespace GamePlay.Effects
{
    public class DebrisBlock : MonoBehaviour
    {
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly System.Collections.Generic.List<DebrisBlock> s_activeBlocks =
            new System.Collections.Generic.List<DebrisBlock>(64);

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
        private bool _registeredForTick;
        private Vector3 _currentVelocity;
        private Vector3 _currentPosition;
        private float _elapsedTime;

        public static void TickActiveBlocks(float dt)
        {
            if (s_activeBlocks.Count == 0) return;

            for (int i = s_activeBlocks.Count - 1; i >= 0; i--)
            {
                var block = s_activeBlocks[i];
                if (block == null || !block._simulating || !block.gameObject.activeInHierarchy)
                {
                    if (block != null) block._registeredForTick = false;
                    RemoveAtSwapBack(i);
                    continue;
                }

                if (block.Step(dt))
                {
                    continue;
                }

                block._registeredForTick = false;
                RemoveAtSwapBack(i);
            }
        }

        public static void ClearActiveBlocks()
        {
            s_activeBlocks.Clear();
            s_activeBlocks.TrimExcess();
        }

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
            RegisterActiveBlock();
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

        private void StopSimulation()
        {
            _simulating = false;
        }

        private void OnDisable()
        {
            StopSimulation();
        }

        private void OnDestroy()
        {
            StopSimulation();
        }

        private void RegisterActiveBlock()
        {
            if (_registeredForTick) return;
            _registeredForTick = true;
            s_activeBlocks.Add(this);
        }

        private static void RemoveAtSwapBack(int index)
        {
            int last = s_activeBlocks.Count - 1;
            if (index < 0 || index > last) return;

            s_activeBlocks[index] = s_activeBlocks[last];
            s_activeBlocks.RemoveAt(last);
        }
    }
}
