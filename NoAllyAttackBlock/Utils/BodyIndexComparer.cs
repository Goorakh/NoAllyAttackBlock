using RoR2;
using System.Collections.Generic;

namespace NoAllyAttackBlock.Utils
{
    public class BodyIndexComparer : IComparer<BodyIndex>
    {
        public static BodyIndexComparer Instance { get; } = new BodyIndexComparer();

        public int Compare(BodyIndex x, BodyIndex y)
        {
            return ((int)x).CompareTo((int)y);
        }
    }
}
