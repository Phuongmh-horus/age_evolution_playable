using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.Characters;
using GamePlay.Items;
using UnityEngine;
using UnityEngine.Serialization;

namespace GamePlay.Managers
{
    public class ConveyorManager : MonoSingleton<ConveyorManager>
    {
        public sealed class BeltItem
        {
            public CharacterUnit Belt;
            public float CurrentZ;
            public int TargetGateIndex;

            public bool IsJumping;
            public Vector3 StartJumpPos;
            public float BeltFixedY;

            public bool IsExiting;
            public Transform ExitTarget;
            public Vector3 ExitStartPos;

            public float JumpTimer;
            public float InverseJumpTime;
            public float JumpHeight;

            public BeltItem Next;
            public BeltItem Previous;

            public void Init(CharacterUnit belt, float z, int gateIndex, bool doJump, float jumpTime, float jumpHeight, float beltYOffset)
            {
                Belt = belt;
                CurrentZ = z;
                TargetGateIndex = gateIndex;
                Next = null;
                Previous = null;
                IsExiting = false;
                ExitTarget = null;

                if (doJump)
                {
                    IsJumping = true;
                    StartJumpPos = belt.Transform.position;
                    BeltFixedY = beltYOffset;
                    JumpTimer = 0f;
                    InverseJumpTime = (jumpTime > 0f) ? 1f / jumpTime : 1f;
                    JumpHeight = jumpHeight;
                    Belt.PlayAnimation(AnimationType.ConveyorJump);
                }
                else
                {
                    IsJumping = false;
                    BeltFixedY = belt.Transform.position.y;
                }
            }

            public void StartExitJump(Transform target, float duration, float height)
            {
                IsExiting = true;
                IsJumping = false;
                ExitTarget = target;
                ExitStartPos = Belt.Transform.position;
                JumpTimer = 0f;
                InverseJumpTime = (duration > 0f) ? 1f / duration : 1f;
                JumpHeight = height;

                if (Belt != null)
                {
                    Belt.PlayAnimation(AnimationType.Idle);
                }
            }

            public void Clear()
            {
                Belt = null;
                CurrentZ = 0f;
                TargetGateIndex = -1;
                IsJumping = false;
                IsExiting = false;
                ExitTarget = null;
                Next = null;
                Previous = null;
            }
        }

        [System.Serializable]
        public class LaneData
        {
            public string laneName = "Lane";
            public float beltXOffset = -8.5f;

            [Header("Gates Logic")]
            public List<CapacityIncreaseGate> soldierIncreaseGates = new List<CapacityIncreaseGate>();

            [HideInInspector] public List<float> gatePositions = new List<float>();
            [HideInInspector] public BeltItem Head;
            [HideInInspector] public BeltItem Tail;
            [HideInInspector] public int Count;

            public void SetupGates()
            {
                gatePositions.Clear();
                soldierIncreaseGates.RemoveAll(x => x == null);
                soldierIncreaseGates.Sort((a, b) => a.Transform.position.z.CompareTo(b.Transform.position.z));

                for (int i = 0; i < soldierIncreaseGates.Count; i++)
                {
                    gatePositions.Add(soldierIncreaseGates[i].Transform.position.z);
                }
            }
        }

        [Header("Conveyor Settings")]
        public List<LaneData> lanes = new List<LaneData>();
        public float beltSpeed = 35.0f;
        public float minSpacing = 2.0f;
        public float beltYOffset = -0.5f;

        [Header("Legacy Fallback (single lane)")]
        [FormerlySerializedAs("beltXOffset")]
        [SerializeField] private float legacyBeltXOffset = -8.5f;

        [Header("Jump Configuration")]
        public float jumpHeight = 2.0f;
        public float entryJumpDuration = 0.5f;
        public float exitJumpDuration = 0.5f;
        public float jumpForwardOffset = 10.0f;
        public float rotationSpeed = 15f;

        private Stack<BeltItem> _pool = new Stack<BeltItem>(400);

        protected override void Awake()
        {
            base.Awake();

            for (int i = 0; i < 200; i++)
            {
                _pool.Push(new BeltItem());
            }

            EnsureLaneSetup();

            for (int i = 0; i < lanes.Count; i++)
            {
                lanes[i].SetupGates();
            }

            enabled = false;
        }

        private void EnsureLaneSetup()
        {
            if (lanes == null)
            {
                lanes = new List<LaneData>();
            }

            if (lanes.Count > 0)
            {
                return;
            }

            lanes.Add(new LaneData
            {
                laneName = "Lane 0",
                beltXOffset = legacyBeltXOffset
            });
        }

        private void Update()
        {
            if (!GameplayManager.IsGameStarted) return;
            if (lanes == null || lanes.Count == 0) return;

            float dt = Time.deltaTime;
            float moveStep = beltSpeed * dt;

            for (int i = 0; i < lanes.Count; i++)
            {
                ProcessLane(lanes[i], dt, moveStep);
            }

            if (!HasActiveBeltItems())
            {
                enabled = false;
            }
        }

        private void OnDisable()
        {
            ClearAllLaneNodesAndBelts();
        }

        private void ProcessLane(LaneData lane, float dt, float moveStep)
        {
            if (lane == null || lane.Head == null) return;

            float worldX = transform.position.x + lane.beltXOffset;
            int gateCount = lane.gatePositions.Count;

            BeltItem current = lane.Head;
            while (current != null)
            {
                BeltItem nextNode = current.Next;

                if (current.IsExiting)
                {
                    if (current.Belt != null && current.ExitTarget != null)
                    {
                        current.JumpTimer += dt;
                        float t = current.JumpTimer * current.InverseJumpTime;
                        Vector3 prevPos = current.Belt.Transform.position;

                        if (t >= 1f)
                        {
                            current.Belt.Transform.position = current.ExitTarget.position;
                            current.Belt.Transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                            RemoveNode(lane, current, keepBeltAlive: true);
                        }
                        else
                        {
                            float ease = t * t * (3f - 2f * t);
                            Vector3 targetPos = current.ExitTarget.position;
                            float x = current.ExitStartPos.x + (targetPos.x - current.ExitStartPos.x) * ease;
                            float z = current.ExitStartPos.z + (targetPos.z - current.ExitStartPos.z) * ease;
                            float baseY = current.ExitStartPos.y + (targetPos.y - current.ExitStartPos.y) * ease;
                            float arcY = 4f * current.JumpHeight * t * (1f - t);

                            Vector3 nextPos = new Vector3(x, baseY + arcY, z);
                            current.Belt.Transform.position = nextPos;
                            RotateYOnly(current.Belt.Transform, prevPos, nextPos);
                        }
                    }
                    else
                    {
                        RemoveNode(lane, current);
                    }

                    current = nextNode;
                    continue;
                }

                if (current.TargetGateIndex < gateCount)
                {
                    float targetGateZ = lane.gatePositions[current.TargetGateIndex];
                    if (!current.IsJumping && current.CurrentZ >= targetGateZ - 0.1f)
                    {
                        bool removeImmediately = TryEnterGate(lane, current);
                        if (removeImmediately)
                        {
                            RemoveNode(lane, current);
                            current = nextNode;
                            continue;
                        }

                        if (current.IsExiting)
                        {
                            current = nextNode;
                            continue;
                        }

                        current.TargetGateIndex++;
                    }
                }

                float proposedZ = current.CurrentZ + moveStep;
                if (current.Previous != null && !current.Previous.IsExiting)
                {
                    float prevZ = current.Previous.CurrentZ;
                    if (proposedZ > prevZ - minSpacing)
                    {
                        proposedZ = prevZ - minSpacing;
                    }
                }
                current.CurrentZ = proposedZ;

                if (current.Belt != null)
                {
                    Vector3 prevPos = current.Belt.Transform.position;
                    Vector3 finalPos;
                    finalPos.x = worldX;
                    finalPos.z = proposedZ;

                    if (current.IsJumping)
                    {
                        current.JumpTimer += dt;
                        float t = current.JumpTimer * current.InverseJumpTime;

                        if (t >= 1f)
                        {
                            current.IsJumping = false;
                            finalPos.y = current.BeltFixedY;
                            current.Belt.Transform.rotation = Quaternion.LookRotation(Vector3.forward);
                        }
                        else
                        {
                            float ease = t * t * (3f - 2f * t);
                            finalPos.x = current.StartJumpPos.x + (worldX - current.StartJumpPos.x) * ease;
                            finalPos.z = current.StartJumpPos.z + (proposedZ - current.StartJumpPos.z) * ease;
                            float baseY = current.StartJumpPos.y + (current.BeltFixedY - current.StartJumpPos.y) * ease;
                            float arcY = 4f * current.JumpHeight * t * (1f - t);
                            finalPos.y = baseY + arcY;
                        }

                        current.Belt.Transform.position = finalPos;
                        if (current.IsJumping)
                        {
                            RotateYOnly(current.Belt.Transform, prevPos, finalPos);
                        }
                    }
                    else
                    {
                        finalPos.y = current.BeltFixedY;
                        current.Belt.Transform.position = finalPos;
                        current.Belt.Transform.rotation = Quaternion.LookRotation(Vector3.forward);
                    }
                }
                else
                {
                    RemoveNode(lane, current);
                }

                current = nextNode;
            }
        }

        private bool TryEnterGate(LaneData lane, BeltItem item)
        {
            int index = item.TargetGateIndex;
            if (index < 0 || index >= lane.soldierIncreaseGates.Count) return false;

            Transform target = lane.soldierIncreaseGates[index].AddCharacter(item.Belt);
            if (target != null)
            {
                item.StartExitJump(target, exitJumpDuration, jumpHeight);
                return false;
            }

            return true;
        }

        public void AddCharactersToBelt(List<CharacterUnit> beltUnits)
        {
            if (beltUnits == null || beltUnits.Count == 0) return;
            EnsureLaneSetup();
            AddCharactersToLane(beltUnits, 0);
        }

        public void AddCharactersToBelt(List<CharacterUnit> beltUnits, Vector3 checkPos)
        {
            if (beltUnits == null || beltUnits.Count == 0) return;
            EnsureLaneSetup();

            int laneIndex = GetClosestLaneIndex(checkPos.x);
            AddCharactersToLane(beltUnits, laneIndex);
        }

        private void AddCharactersToLane(List<CharacterUnit> beltUnits, int laneIndex)
        {
            if (lanes == null || lanes.Count == 0) return;
            if (laneIndex < 0 || laneIndex >= lanes.Count)
            {
                Debug.LogError($"[ConveyorManager] Invalid lane index: {laneIndex}");
                return;
            }

            LaneData lane = lanes[laneIndex];
            BeltItem lastItemRef = lane.Tail;
            float distDriftDuringJump = beltSpeed * entryJumpDuration;
            if (!enabled) enabled = true;

            for (int i = 0; i < beltUnits.Count; i++)
            {
                CharacterUnit unit = beltUnits[i];
                if (unit == null) continue;

                float currentUnitZ = unit.Transform.position.z;
                float targetZ = currentUnitZ + jumpForwardOffset;

                int nextGateIndex = FindGateIndexBinary(lane.gatePositions, currentUnitZ);
                if (nextGateIndex < lane.gatePositions.Count)
                {
                    float gateZ = lane.gatePositions[nextGateIndex];
                    float maxLandingZ = gateZ - distDriftDuringJump - 0.2f;
                    if (targetZ > maxLandingZ)
                    {
                        targetZ = maxLandingZ;
                    }
                }

                float startZ = targetZ;
                if (lastItemRef != null && !lastItemRef.IsExiting)
                {
                    if (startZ > lastItemRef.CurrentZ - minSpacing)
                    {
                        startZ = lastItemRef.CurrentZ - minSpacing;
                    }
                }

                int targetIndex = FindGateIndexBinary(lane.gatePositions, startZ);
                BeltItem newItem = GetFromPool();
                newItem.Init(unit, startZ, targetIndex, true, entryJumpDuration, jumpHeight, beltYOffset);

                AddLastNode(lane, newItem);
                lastItemRef = newItem;
            }
        }

        private int GetClosestLaneIndex(float unitWorldX)
        {
            if (lanes == null || lanes.Count == 0) return 0;
            if (lanes.Count == 1) return 0;

            int closestIndex = 0;
            float minDiff = float.MaxValue;
            float managerWorldX = transform.position.x;

            for (int i = 0; i < lanes.Count; i++)
            {
                float laneWorldX = managerWorldX + lanes[i].beltXOffset;
                float diff = Mathf.Abs(laneWorldX - unitWorldX);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        public void SetGatePositions(List<ItemUnit> itemUnits)
        {
            EnsureLaneSetup();
            if (lanes == null || lanes.Count == 0) return;

            for (int i = 0; i < lanes.Count; i++)
            {
                SetGatePositions(itemUnits, i);
            }
        }

        public void SetGatePositions(List<ItemUnit> itemUnits, int laneIndex)
        {
            EnsureLaneSetup();
            if (laneIndex < 0 || laneIndex >= lanes.Count) return;

            LaneData lane = lanes[laneIndex];
            lane.soldierIncreaseGates.Clear();
            if (itemUnits == null)
            {
                lane.SetupGates();
                return;
            }

            for (int i = 0; i < itemUnits.Count; i++)
            {
                if (itemUnits[i] is CapacityIncreaseGate g)
                {
                    lane.soldierIncreaseGates.Add(g);
                }
            }

            lane.SetupGates();
        }

        public bool HasGateAhead(float currentZ)
        {
            EnsureLaneSetup();
            if (lanes == null || lanes.Count == 0) return false;
            return CheckGateInLane(lanes[0], currentZ);
        }

        public bool HasGateAhead(Vector3 position)
        {
            EnsureLaneSetup();
            if (lanes == null || lanes.Count == 0) return false;
            int laneIndex = GetClosestLaneIndex(position.x);
            return CheckGateInLane(lanes[laneIndex], position.z);
        }

        public CapacityIncreaseGate GetNextGate(float currentZ)
        {
            EnsureLaneSetup();
            if (lanes == null || lanes.Count == 0) return null;
            return GetNextGateInLane(lanes[0], currentZ);
        }

        public bool IsGateNearby(float currentZ, float checkDistance)
        {
            EnsureLaneSetup();
            if (lanes == null || lanes.Count == 0) return false;
            return IsGateNearbyInLane(lanes[0], currentZ, checkDistance);
        }

        private bool CheckGateInLane(LaneData lane, float z)
        {
            if (lane == null) return false;
            int index = FindGateIndexBinary(lane.gatePositions, z);
            return index < lane.gatePositions.Count;
        }

        private CapacityIncreaseGate GetNextGateInLane(LaneData lane, float z)
        {
            if (lane == null) return null;
            int index = FindGateIndexBinary(lane.gatePositions, z);
            if (index < lane.soldierIncreaseGates.Count) return lane.soldierIncreaseGates[index];
            return null;
        }

        private bool IsGateNearbyInLane(LaneData lane, float z, float dist)
        {
            if (lane == null) return false;
            int index = FindGateIndexBinary(lane.gatePositions, z);
            if (index < lane.gatePositions.Count)
            {
                return (lane.gatePositions[index] - z) <= dist;
            }
            return false;
        }

        private int FindGateIndexBinary(List<float> positions, float z)
        {
            if (positions == null) return 0;
            int index = positions.BinarySearch(z);
            return index >= 0 ? index : ~index;
        }

        private BeltItem GetFromPool() => (_pool.Count > 0) ? _pool.Pop() : new BeltItem();

        private void ReturnToPool(BeltItem item)
        {
            item.Clear();
            _pool.Push(item);
        }

        private void AddLastNode(LaneData lane, BeltItem newItem)
        {
            if (lane.Head == null)
            {
                lane.Head = newItem;
                lane.Tail = newItem;
            }
            else
            {
                lane.Tail.Next = newItem;
                newItem.Previous = lane.Tail;
                lane.Tail = newItem;
            }
            lane.Count++;
        }

        private void RemoveNode(LaneData lane, BeltItem item)
        {
            RemoveNode(lane, item, keepBeltAlive: false);
        }

        private void RemoveNode(LaneData lane, BeltItem item, bool keepBeltAlive)
        {
            if (item == null || lane == null) return;

            if (!keepBeltAlive)
            {
                RecycleBeltUnit(item.Belt);
            }

            if (item.Previous != null) item.Previous.Next = item.Next; else lane.Head = item.Next;
            if (item.Next != null) item.Next.Previous = item.Previous; else lane.Tail = item.Previous;

            lane.Count--;
            ReturnToPool(item);
        }

        private static void RecycleBeltUnit(CharacterUnit unit)
        {
            if (unit == null) return;
            if (!unit.gameObject.activeInHierarchy) return;

            unit.Transform.parent = null;
            unit.Transform.localScale = Vector3.one;
            unit.RecycleImmediate(false);
        }

        private void ClearAllLaneNodesAndBelts()
        {
            if (lanes == null || lanes.Count == 0) return;

            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                var lane = lanes[laneIndex];
                if (lane == null) continue;

                var node = lane.Head;
                while (node != null)
                {
                    var next = node.Next;
                    RecycleBeltUnit(node.Belt);
                    ReturnToPool(node);
                    node = next;
                }

                lane.Head = null;
                lane.Tail = null;
                lane.Count = 0;
            }

            enabled = false;
        }

        private bool HasActiveBeltItems()
        {
            if (lanes == null) return false;
            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                if (lane != null && lane.Head != null)
                    return true;
            }

            return false;
        }

        private void RotateYOnly(Transform t, Vector3 currentPos, Vector3 targetPos)
        {
            Vector3 direction = targetPos - currentPos;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                t.rotation = Quaternion.Slerp(t.rotation, targetRot, Time.deltaTime * rotationSpeed);
            }
        }
    }
}
