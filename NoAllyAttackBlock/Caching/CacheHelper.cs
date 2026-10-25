using UnityEngine;

using Path = System.IO.Path;

namespace NoAllyAttackBlock.Caching
{
    internal static class CacheHelper
    {
        private static readonly string _persistentDataDirectory = Path.Combine(Application.persistentDataPath, NoAllyAttackBlockPlugin.PluginGUID);
        private static readonly string _cacheDirectory = Path.Combine(_persistentDataDirectory, "cache");

        public static string GetCacheDirectory(string cacheFolderName)
        {
            return Path.Combine(_cacheDirectory, cacheFolderName);
        }
    }
}