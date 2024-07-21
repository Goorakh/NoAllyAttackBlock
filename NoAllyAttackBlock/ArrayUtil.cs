using System;

namespace NoAllyAttackBlock
{
    public static class ArrayUtil
    {
        public static void Append<T>(ref T[] a, T[] b)
        {
            if (a == null)
            {
                a = b;
                return;
            }

            if (a.Length == 0)
            {
                a = b ?? [];
                return;
            }

            if (b == null || b.Length == 0)
                return;

            T[] combined = new T[a.Length + b.Length];

            Array.Copy(a, 0, combined, 0, a.Length);
            Array.Copy(b, 0, combined, a.Length, b.Length);

            a = combined;
        }
    }
}
