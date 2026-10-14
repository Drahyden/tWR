using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWR.Items
{
    /// <summary>
    /// The player's item data (D-037: up to 20 item types, max 99 each). Reserve ammo lives here as an
    /// "Ammo" stack (D-050), not in a separate counter, so guns, pickups and the future pause-menu grid
    /// (D-087) all read the same numbers. Data only: no UI in this step.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        /// <summary>D-037: at most this many different item types.</summary>
        public const int MaxItemTypes = 20;
        /// <summary>D-037: no stack can hold more than this.</summary>
        public const int MaxStack = 99;

        [Serializable]
        public struct ItemStack
        {
            public ItemData item;
            public int count;
        }

        [SerializeField] List<ItemStack> stacks = new List<ItemStack>(MaxItemTypes);

        /// <summary>Raised whenever a stack changes: (item, new count).</summary>
        public event Action<ItemData, int> Changed;

        public IReadOnlyList<ItemStack> Stacks => stacks;

        /// <summary>How many of this item we carry (0 if none).</summary>
        public int Count(ItemData item)
        {
            int index = IndexOf(item);
            return index >= 0 ? stacks[index].count : 0;
        }

        /// <summary>How many more of this item fit before the stack cap.</summary>
        public int SpaceFor(ItemData item)
        {
            if (item == null) return 0;
            int index = IndexOf(item);
            if (index < 0 && stacks.Count >= MaxItemTypes) return 0; // no free type slot
            return Mathf.Max(0, Cap(item) - (index >= 0 ? stacks[index].count : 0));
        }

        /// <summary>Adds up to "amount". Returns how many were actually added (may be less at the cap).</summary>
        public int Add(ItemData item, int amount)
        {
            int added = Mathf.Min(amount, SpaceFor(item));
            if (added <= 0) return 0;

            int index = IndexOf(item);
            if (index < 0)
            {
                stacks.Add(new ItemStack { item = item, count = 0 });
                index = stacks.Count - 1;
            }
            ItemStack stack = stacks[index];
            stack.count += added;
            stacks[index] = stack;
            Changed?.Invoke(item, stack.count);
            return added;
        }

        /// <summary>Takes up to "amount" away. Returns how many were actually removed.</summary>
        public int Remove(ItemData item, int amount)
        {
            int index = IndexOf(item);
            if (index < 0 || amount <= 0) return 0;

            ItemStack stack = stacks[index];
            int removed = Mathf.Min(amount, stack.count);
            stack.count -= removed;
            if (stack.count <= 0) stacks.RemoveAt(index); // frees the type slot
            else stacks[index] = stack;
            Changed?.Invoke(item, stack.count);
            return removed;
        }

        /// <summary>Remove every stack (run start, D-162). Raises Changed with count 0 for each removed item.</summary>
        public void Clear()
        {
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                ItemData item = stacks[i].item;
                stacks.RemoveAt(i);
                Changed?.Invoke(item, 0);
            }
        }

        static int Cap(ItemData item) => Mathf.Clamp(item.maxStack, 1, MaxStack);

        int IndexOf(ItemData item)
        {
            if (item == null) return -1;
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i].item == item) return i;
            return -1;
        }
    }
}
