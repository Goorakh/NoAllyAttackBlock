using System;
using System.IO;
using System.Reflection;

namespace NoAllyAttackBlock.Utils.Extensions
{
    internal static partial class SerializationExtensions
    {
        public static void WriteGuid(this BinaryWriter writer, in Guid guid)
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes);
            writer.Write(bytes);
        }

        public static Guid ReadGuid(this BinaryReader reader)
        {
            Span<byte> bytes = stackalloc byte[16];
            reader.Read(bytes);
            return new Guid(bytes);
        }

        public static void WriteMember<TMember>(this BinaryWriter writer, TMember memberInfo)
            where TMember : MemberInfo
        {
            writer.Write(memberInfo.MetadataToken);
        }

        public static MethodBase ReadMethodBase(this BinaryReader reader, Assembly assembly)
        {
            int methodToken = reader.ReadInt32();

            MethodBase methodBase = assembly.ManifestModule.ResolveMethod(methodToken);
            if (methodBase != null)
            {
                return methodBase;
            }
            else
            {
                Log.Warning($"Failed to resolve method with token {methodToken} in {assembly.FullName}");
                return null;
            }
        }

        public static MethodInfo ReadMethodInfo(this BinaryReader reader, Assembly assembly)
        {
            int methodToken = reader.ReadInt32();

            MethodBase methodBase = assembly.ManifestModule.ResolveMethod(methodToken);
            if (methodBase is MethodInfo methodInfo)
            {
                return methodInfo;
            }
            else
            {
                Log.Warning($"Failed to resolve method with token {methodToken} in {assembly.FullName}");
                return null;
            }
        }
    }
}
