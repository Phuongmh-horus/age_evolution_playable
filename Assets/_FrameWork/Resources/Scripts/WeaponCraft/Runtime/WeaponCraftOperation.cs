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
            return new WeaponCraftOperation(WeaponCraftOperationType.AddItem, item, new List<WeaponItem>(), targetIndex, flyFromPosition);
        }

        public static WeaponCraftOperation CreateMerge(WeaponItem resultItem, List<WeaponItem> sourceItems, int targetIndex)
        {
            var sources = sourceItems == null ? new List<WeaponItem>() : new List<WeaponItem>(sourceItems);
            return new WeaponCraftOperation(WeaponCraftOperationType.Merge, resultItem, sources, targetIndex, Vector3.zero);
        }
    }
}
