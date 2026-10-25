using System.Collections.Generic;

namespace NoAllyAttackBlock.Utils.Extensions
{
    internal static class CollectionExtensions
    {
        public static TValue GetOrAddNew<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
            where TValue : new()
        {
            if (!dictionary.TryGetValue(key, out TValue value))
            {
                dictionary.Add(key, value = new TValue());
            }

            return value;
        }
    }
}
