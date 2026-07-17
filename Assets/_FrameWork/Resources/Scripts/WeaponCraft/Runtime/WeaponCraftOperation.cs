using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeaponCraft
{
    public enum WeaponCraftOperationType
    {
        AddItem,
        Merge
    }

    [Serializable]
    public sealed class WeaponCraftOperation
    {
        [SerializeField] private WeaponCraftOperationType type;
        [SerializeField] private WeaponItem item;
        [SerializeField] private List<WeaponItem> sourceItems = new List<WeaponItem>();
        [SerializeField] private int targetIndex = -1;
        [SerializeField] private Vector3 flyFromPosition;

        public WeaponCraftOperationType Type => type;
        public WeaponItem Item => item;
        public List<WeaponItem> SourceItems => sourceItems;
        public int TargetIndex => targetIndex;
        public Vector3 FlyFromPosition => flyFromPosition;

        private WeaponCraftOperation()
        {
        }

        private WeaponCraftOperation(WeaponCraftOperationType type, WeaponItem item, List<WeaponItem> sourceItems, int targetIndex, Vector3 flyFromPosition)
        {
            this.type = type;
            this.item = item;
            this.sourceItems = sourceItems ?? new List<WeaponItem>();
            this.targetIndex = targetIndex;
            this.flyFromPosition = flyFromPosition;
        }

        public static WeaponCraftOperation CreateAdd(WeaponItem item, Vector3 flyFromPosition, int targetIndex)
        {
            var op = _pool.Count > 0 ? _pool.Pop() : new WeaponCraftOperation();
            op.type = WeaponCraftOperationType.AddItem;
            op.item = item;
            if (op.sourceItems == null) op.sourceItems = new List<WeaponItem>();
            else op.sourceItems.Clear();
            op.targetIndex = targetIndex;
            op.flyFromPosition = flyFromPosition;
            return op;
        }

        public static WeaponCraftOperation CreateMerge(WeaponItem resultItem, List<WeaponItem> sourceItems, int targetIndex)
        {
            var op = _pool.Count > 0 ? _pool.Pop() : new WeaponCraftOperation();
            op.type = WeaponCraftOperationType.Merge;
            op.item = resultItem;
            if (op.sourceItems == null) op.sourceItems = new List<WeaponItem>();
            else op.sourceItems.Clear();
            
            if (sourceItems != null)
            {
                op.sourceItems.AddRange(sourceItems);
            }
            
            op.targetIndex = targetIndex;
            op.flyFromPosition = Vector3.zero;
            return op;
        }
        
        private static readonly Stack<WeaponCraftOperation> _pool = new Stack<WeaponCraftOperation>(32);

        public void Release()
        {
            item = null;
            sourceItems.Clear();
            _pool.Push(this);
        }
    }
}
