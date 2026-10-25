using NoAllyAttackBlock.Caching;
using System;
using System.IO;
using System.Reflection;

namespace NoAllyAttackBlock.Caching
{
    internal readonly struct AssemblyCacheHeader
    {
        public readonly Guid ManifestVersionId;

        public AssemblyCacheHeader(Guid manifestVersionId)
        {
            ManifestVersionId = manifestVersionId;
        }

        public static AssemblyCacheHeader FromAssembly(Assembly assembly)
        {
            return new AssemblyCacheHeader(assembly.ManifestModule.ModuleVersionId);
        }
    }
}

namespace NoAllyAttackBlock.Utils.Extensions
{
    partial class SerializationExtensions
    {
        public static void WriteAssemblyCacheHeader(this BinaryWriter writer, in AssemblyCacheHeader assemblyCacheHeader)
        {
            writer.WriteGuid(assemblyCacheHeader.ManifestVersionId);
        }

        public static AssemblyCacheHeader ReadAssemblyCacheHeader(this BinaryReader reader)
        {
            Guid manifestVersionId = reader.ReadGuid();
            return new AssemblyCacheHeader(manifestVersionId);
        }
    }
}
