using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatentMarker.ManagedSemanticDiff;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ManagedSemanticDiff <left.dll> <right.dll> [--json <path>] [--markdown <path>] [--fail-on-diff]");
            return 2;
        }

        string leftPath = Path.GetFullPath(args[0]);
        string rightPath = Path.GetFullPath(args[1]);
        string? jsonPath = null;
        string? markdownPath = null;
        bool failOnDiff = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length) jsonPath = args[++i];
            else if (args[i] == "--markdown" && i + 1 < args.Length) markdownPath = args[++i];
            else if (args[i] == "--fail-on-diff") failOnDiff = true;
            else
            {
                Console.Error.WriteLine("Unknown argument: " + args[i]);
                return 2;
            }
        }

        try
        {
            AssemblySnapshot left = AssemblySnapshot.Load(leftPath);
            AssemblySnapshot right = AssemblySnapshot.Load(rightPath);
            ComparisonReport report = ComparisonReport.Create(left, right);

            string json = JsonSerializer.Serialize(report, JsonOptions);
            string markdown = report.ToMarkdown();
            if (!string.IsNullOrEmpty(jsonPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
                File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
            }
            if (!string.IsNullOrEmpty(markdownPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markdownPath))!);
                File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
            }

            Console.WriteLine(markdown);
            bool hasSemanticDiff = report.AddedMethods.Count > 0
                || report.RemovedMethods.Count > 0
                || report.ChangedMethods.Count > 0
                || report.AddedTypes.Count > 0
                || report.RemovedTypes.Count > 0;
            return failOnDiff && hasSemanticDiff ? 3 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

internal sealed class AssemblySnapshot
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public required string Mvid { get; init; }
    public required string AssemblyVersion { get; init; }
    public required string InformationalVersion { get; init; }
    public required string ModuleName { get; init; }
    public required SortedDictionary<string, string> TypeByMethodKey { get; init; }
    public required SortedDictionary<string, MethodSnapshot> Methods { get; init; }

    public static AssemblySnapshot Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            throw new InvalidDataException("Not a managed PE image: " + path);
        MetadataReader reader = pe.GetMetadataReader();
        var provider = new TypeNameProvider(reader);
        var methods = new SortedDictionary<string, MethodSnapshot>(StringComparer.Ordinal);
        var typeByMethod = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            string typeName = TypeNameProvider.GetTypeDefinitionName(reader, typeHandle);
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                string signature;
                try
                {
                    MethodSignature<string> decoded = method.DecodeSignature(provider, null);
                    signature = signatureLabel(decoded, provider);
                }
                catch
                {
                    signature = "sig:" + HashHex(reader.GetBlobBytes(method.Signature));
                }

                string key = typeName + "::" + name + signature;
                MethodSnapshot snapshot = MethodSnapshot.Create(reader, pe, provider, methodHandle, key);
                if (methods.ContainsKey(key))
                    key += " #" + (++snapshot.DuplicateOrdinal);
                methods[key] = snapshot;
                typeByMethod[key] = typeName;
            }
        }

        string assemblyVersion = reader.IsAssembly
            ? reader.GetAssemblyDefinition().Version.ToString()
            : "";
        string informationalVersion = ReadInformationalVersion(reader);
        return new AssemblySnapshot
        {
            Path = System.IO.Path.GetFullPath(path),
            Sha256 = HashHex(bytes),
            Size = bytes.LongLength,
            Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D"),
            AssemblyVersion = assemblyVersion,
            InformationalVersion = informationalVersion,
            ModuleName = reader.GetString(reader.GetModuleDefinition().Name),
            TypeByMethodKey = typeByMethod,
            Methods = methods
        };
    }

    private static string signatureLabel(MethodSignature<string> sig, TypeNameProvider provider)
    {
        string parameters = string.Join(",", sig.ParameterTypes.Select(Escape));
        string generic = sig.GenericParameterCount > 0
            ? "<" + sig.GenericParameterCount.ToString(CultureInfo.InvariantCulture) + ">"
            : "";
        return generic + "(" + parameters + ")->" + Escape(sig.ReturnType);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static string ReadInformationalVersion(MetadataReader reader)
    {
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attr = reader.GetCustomAttribute(handle);
            if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
            MemberReference ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
            TypeReference parent = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
            if (reader.GetString(parent.Namespace) != "System.Reflection" ||
                reader.GetString(parent.Name) != "AssemblyInformationalVersionAttribute")
                continue;
            BlobReader blob = reader.GetBlobReader(attr.Value);
            if (blob.ReadUInt16() != 1) continue; // prolog
            return blob.ReadSerializedString() ?? "";
        }
        return "";
    }

    internal static string HashHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

internal sealed class MethodSnapshot
{
    public required string Key { get; init; }
    public required string TypeName { get; init; }
    public required string Name { get; init; }
    public required string Signature { get; init; }
    public required string IlSha256 { get; init; }
    public required string SemanticSha256 { get; init; }
    public required int IlSize { get; init; }
    public required int InstructionCount { get; init; }
    public required string? Locals { get; init; }
    public required string BodyStatus { get; init; }
    public int DuplicateOrdinal { get; set; }

    public static MethodSnapshot Create(
        MetadataReader reader,
        PEReader pe,
        TypeNameProvider provider,
        MethodDefinitionHandle methodHandle,
        string key)
    {
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        string typeName = TypeNameProvider.GetTypeDefinitionName(reader, method.GetDeclaringType());
        string name = reader.GetString(method.Name);
        string signature;
        try
        {
            signature = string.Join(",", method.DecodeSignature(provider, null).ParameterTypes);
        }
        catch
        {
            signature = "sig:" + AssemblySnapshot.HashHex(reader.GetBlobBytes(method.Signature));
        }

        if (method.RelativeVirtualAddress == 0)
        {
            return new MethodSnapshot
            {
                Key = key,
                TypeName = typeName,
                Name = name,
                Signature = signature,
                IlSha256 = "",
                SemanticSha256 = AssemblySnapshot.HashHex(Encoding.UTF8.GetBytes("no-body")),
                IlSize = 0,
                InstructionCount = 0,
                Locals = null,
                BodyStatus = "no-body"
            };
        }

        MethodBodyBlock body;
        try
        {
            body = pe.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch
        {
            return new MethodSnapshot
            {
                Key = key,
                TypeName = typeName,
                Name = name,
                Signature = signature,
                IlSha256 = "",
                SemanticSha256 = AssemblySnapshot.HashHex(Encoding.UTF8.GetBytes("unreadable-body")),
                IlSize = 0,
                InstructionCount = 0,
                Locals = null,
                BodyStatus = "unreadable-body"
            };
        }

        byte[] il = body.GetILBytes();
        string locals = null;
        if (!body.LocalSignature.IsNil)
        {
            try
            {
                StandaloneSignature sig = reader.GetStandaloneSignature(body.LocalSignature);
                locals = string.Join(";", sig.DecodeLocalSignature(provider, null));
            }
            catch
            {
                locals = "unresolved";
            }
        }

        string semantic = NormalizeIl(reader, body, il, provider);
        return new MethodSnapshot
        {
            Key = key,
            TypeName = typeName,
            Name = name,
            Signature = signature,
            IlSha256 = AssemblySnapshot.HashHex(il),
            SemanticSha256 = AssemblySnapshot.HashHex(Encoding.UTF8.GetBytes(semantic)),
            IlSize = il.Length,
            InstructionCount = CountInstructions(il),
            Locals = locals,
            BodyStatus = "present"
        };
    }

    private static string NormalizeIl(
        MetadataReader reader,
        MethodBodyBlock body,
        byte[] il,
        TypeNameProvider provider)
    {
        StringBuilder sb = new();
        sb.Append("initlocals=").Append(body.LocalVariablesInitialized ? "1" : "0");
        if (body.LocalSignature.IsNil)
        {
            sb.Append("\nlocals=<none>");
        }
        else
        {
            try
            {
                StandaloneSignature sig = reader.GetStandaloneSignature(body.LocalSignature);
                sb.Append("\nlocals=").Append(string.Join(";", sig.DecodeLocalSignature(provider, null)));
            }
            catch
            {
                sb.Append("\nlocals=<unresolved>");
            }
        }

        int offset = 0;
        int index = 0;
        while (offset < il.Length)
        {
            int instructionOffset = offset;
            ushort value;
            byte first = il[offset++];
            if (first == 0xFE)
            {
                if (offset >= il.Length) { sb.Append("\nTRUNCATED_PREFIX"); break; }
                value = (ushort)(0xFE00 | il[offset++]);
            }
            else
            {
                value = first;
            }

            if (!OpCodeTable.TryGetValue(value, out OpCode op))
            {
                sb.Append("\nIL_").Append(instructionOffset.ToString("X4")).Append(": <unknown-").Append(value.ToString("X4")).Append('>');
                break;
            }

            sb.Append("\nIL_").Append(instructionOffset.ToString("X4")).Append(": ").Append(op.Name);
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                    sb.Append(' ').Append(((sbyte)il[offset]).ToString(CultureInfo.InvariantCulture));
                    offset += 1;
                    break;
                case OperandType.InlineI:
                    sb.Append(' ').Append(ReadInt32(il, ref offset).ToString(CultureInfo.InvariantCulture));
                    break;
                case OperandType.InlineI8:
                    sb.Append(' ').Append(ReadInt64(il, ref offset).ToString(CultureInfo.InvariantCulture));
                    break;
                case OperandType.ShortInlineR:
                    sb.Append(" bits=").Append(ReadInt32(il, ref offset).ToString("X8"));
                    break;
                case OperandType.InlineR:
                    sb.Append(" bits=").Append(ReadInt64(il, ref offset).ToString("X16"));
                    break;
                case OperandType.ShortInlineVar:
                    sb.Append(" var=").Append(il[offset++].ToString(CultureInfo.InvariantCulture));
                    break;
                case OperandType.InlineVar:
                    sb.Append(" var=").Append(ReadUInt16(il, ref offset).ToString(CultureInfo.InvariantCulture));
                    break;
                case OperandType.ShortInlineBrTarget:
                    {
                        int delta = (sbyte)il[offset];
                        offset += 1;
                        int target = offset + delta;
                        sb.Append(" target=IL_").Append(target.ToString("X4"));
                        break;
                    }
                case OperandType.InlineBrTarget:
                    {
                        int delta = ReadInt32(il, ref offset);
                        int target = offset + delta;
                        sb.Append(" target=IL_").Append(target.ToString("X4"));
                        break;
                    }
                case OperandType.InlineSwitch:
                    {
                        int count = ReadInt32(il, ref offset);
                        if (count < 0 || count > 1_000_000) { sb.Append(" switch=<invalid>"); offset = il.Length; break; }
                        var deltas = new int[count];
                        for (int i = 0; i < count; i++) deltas[i] = ReadInt32(il, ref offset);
                        int baseOffset = offset;
                        sb.Append(" switch=(");
                        for (int i = 0; i < count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append("IL_").Append((baseOffset + deltas[i]).ToString("X4"));
                        }
                        sb.Append(')');
                        break;
                    }
                case OperandType.InlineString:
                    {
                        int token = ReadInt32(il, ref offset);
                        sb.Append(" str=").Append(ResolveString(reader, token));
                        break;
                    }
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                    {
                        int token = ReadInt32(il, ref offset);
                        sb.Append(" tok=").Append(ResolveToken(reader, provider, token));
                        break;
                    }
                default:
                    sb.Append(" <unsupported-operand:").Append(op.OperandType).Append('>');
                    offset = il.Length;
                    break;
            }
            index++;
        }

        foreach (ExceptionRegion region in body.ExceptionRegions)
        {
            sb.Append("\nexception kind=").Append(region.Kind)
              .Append(" try=").Append(region.TryOffset.ToString("X4")).Append(':').Append(region.TryLength)
              .Append(" handler=").Append(region.HandlerOffset.ToString("X4")).Append(':').Append(region.HandlerLength);
            if (!region.CatchType.IsNil)
                sb.Append(" catch=").Append(ResolveEntityType(reader, provider, region.CatchType));
        }
        sb.Append("\ninstructions=").Append(index.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static int CountInstructions(byte[] il)
    {
        int offset = 0, count = 0;
        while (offset < il.Length)
        {
            byte first = il[offset++];
            ushort value = first == 0xFE ? (ushort)(0xFE00 | (offset < il.Length ? il[offset++] : 0)) : first;
            if (!OpCodeTable.TryGetValue(value, out OpCode op)) break;
            offset = SkipOperand(il, offset, op.OperandType);
            count++;
        }
        return count;
    }

    private static int SkipOperand(byte[] il, int offset, OperandType type)
    {
        return type switch
        {
            OperandType.InlineNone => offset,
            OperandType.ShortInlineI or OperandType.ShortInlineVar => offset + 1,
            OperandType.InlineVar => offset + 2,
            OperandType.InlineI or OperandType.ShortInlineR or OperandType.InlineString or OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineSig or OperandType.InlineBrTarget => offset + 4,
            OperandType.InlineI8 or OperandType.InlineR => offset + 8,
            OperandType.ShortInlineBrTarget => offset + 1,
            OperandType.InlineSwitch => offset + 4 + Math.Max(0, ReadInt32(il, ref offset)) * 4,
            _ => il.Length
        };
    }

    private static int ReadInt32(byte[] bytes, ref int offset)
    {
        int value = BitConverter.ToInt32(bytes, offset);
        offset += 4;
        return value;
    }
    private static long ReadInt64(byte[] bytes, ref int offset)
    {
        long value = BitConverter.ToInt64(bytes, offset);
        offset += 8;
        return value;
    }
    private static ushort ReadUInt16(byte[] bytes, ref int offset)
    {
        ushort value = BitConverter.ToUInt16(bytes, offset);
        offset += 2;
        return value;
    }

    private static string ResolveString(MetadataReader reader, int token)
    {
        if ((token >> 24) != 0x70) return "0x" + token.ToString("X8");
        try
        {
            string value = reader.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF));
            return "\"" + value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + "\"";
        }
        catch { return "0x" + token.ToString("X8"); }
    }

    private static string ResolveToken(MetadataReader reader, TypeNameProvider provider, int token)
    {
        try
        {
            EntityHandle handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => "type " + TypeNameProvider.GetTypeDefinitionName(reader, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => "type " + TypeNameProvider.GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => "type " + ResolveType(reader, provider, (TypeSpecificationHandle)handle),
                HandleKind.MethodDefinition => "method " + ResolveMethodDefinition(reader, provider, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => "member " + ResolveMemberReference(reader, provider, (MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => "methodspec " + ResolveMethodSpecification(reader, provider, (MethodSpecificationHandle)handle),
                HandleKind.FieldDefinition => "field " + ResolveFieldDefinition(reader, provider, (FieldDefinitionHandle)handle),
                HandleKind.StandaloneSignature => "sig " + ResolveStandaloneSignature(reader, provider, (StandaloneSignatureHandle)handle),
                HandleKind.ModuleReference => "module " + reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name),
                _ => "token 0x" + token.ToString("X8")
            };
        }
        catch
        {
            return "token 0x" + token.ToString("X8");
        }
    }

    private static string ResolveMethodDefinition(MetadataReader reader, TypeNameProvider provider, MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        string type = TypeNameProvider.GetTypeDefinitionName(reader, method.GetDeclaringType());
        string name = reader.GetString(method.Name);
        try
        {
            MethodSignature<string> sig = method.DecodeSignature(provider, null);
            return type + "::" + name + "(" + string.Join(",", sig.ParameterTypes) + ")->" + sig.ReturnType;
        }
        catch
        {
            return type + "::" + name;
        }
    }

    private static string ResolveFieldDefinition(MetadataReader reader, TypeNameProvider provider, FieldDefinitionHandle handle)
    {
        FieldDefinition field = reader.GetFieldDefinition(handle);
        string type = TypeNameProvider.GetTypeDefinitionName(reader, field.GetDeclaringType());
        string name = reader.GetString(field.Name);
        try { return type + "::" + name + ":" + field.DecodeSignature(provider, null); }
        catch { return type + "::" + name; }
    }

    private static string ResolveMemberReference(MetadataReader reader, TypeNameProvider provider, MemberReferenceHandle handle)
    {
        MemberReference member = reader.GetMemberReference(handle);
        string parent = ResolveEntityForMember(reader, provider, member.Parent);
        string name = reader.GetString(member.Name);
        try
        {
            if (member.GetKind() == MemberReferenceKind.Method)
            {
                MethodSignature<string> sig = member.DecodeMethodSignature(provider, null);
                return parent + "::" + name + "(" + string.Join(",", sig.ParameterTypes) + ")->" + sig.ReturnType;
            }
            return parent + "::" + name + ":" + member.DecodeFieldSignature(provider, null);
        }
        catch
        {
            return parent + "::" + name;
        }
    }

    private static string ResolveEntityForMember(MetadataReader reader, TypeNameProvider provider, EntityHandle parent)
    {
        return parent.Kind switch
        {
            HandleKind.TypeDefinition => TypeNameProvider.GetTypeDefinitionName(reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeReference => TypeNameProvider.GetTypeReferenceName(reader, (TypeReferenceHandle)parent),
            HandleKind.TypeSpecification => ResolveType(reader, provider, (TypeSpecificationHandle)parent),
            HandleKind.MethodDefinition => ResolveMethodDefinition(reader, provider, (MethodDefinitionHandle)parent),
            HandleKind.ModuleReference => reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)parent).Name),
            _ => parent.Kind.ToString()
        };
    }

    private static string ResolveMethodSpecification(MetadataReader reader, TypeNameProvider provider, MethodSpecificationHandle handle)
    {
        MethodSpecification spec = reader.GetMethodSpecification(handle);
        string method = spec.Method.Kind switch
        {
            HandleKind.MethodDefinition => ResolveMethodDefinition(reader, provider, (MethodDefinitionHandle)spec.Method),
            HandleKind.MemberReference => ResolveMemberReference(reader, provider, (MemberReferenceHandle)spec.Method),
            _ => spec.Method.Kind.ToString()
        };
        try
        {
            return method + "<" + string.Join(",", spec.DecodeSignature(provider, null)) + ">";
        }
        catch { return method; }
    }

    private static string ResolveStandaloneSignature(MetadataReader reader, TypeNameProvider provider, StandaloneSignatureHandle handle)
    {
        try
        {
            StandaloneSignature sig = reader.GetStandaloneSignature(handle);
            return "sig:" + AssemblySnapshot.HashHex(reader.GetBlobBytes(sig.Signature));
        }
        catch { return "sig-unresolved"; }
    }

    private static string ResolveEntityType(MetadataReader reader, TypeNameProvider provider, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeNameProvider.GetTypeDefinitionName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => TypeNameProvider.GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => ResolveType(reader, provider, (TypeSpecificationHandle)handle),
            _ => handle.Kind.ToString()
        };
    }

    private static string ResolveType(MetadataReader reader, TypeNameProvider provider, TypeSpecificationHandle handle)
    {
        try { return reader.GetTypeSpecification(handle).DecodeSignature(provider, null); }
        catch { return "typespec-unresolved"; }
    }

    private static readonly Dictionary<ushort, OpCode> OpCodeTable = BuildOpCodeTable();
    private static Dictionary<ushort, OpCode> BuildOpCodeTable()
    {
        var map = new Dictionary<ushort, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode)) continue;
            OpCode op = (OpCode)field.GetValue(null)!;
            map[(ushort)op.Value] = op;
        }
        return map;
    }
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;
    public TypeNameProvider(MetadataReader reader) => _reader = reader;

    public static string GetTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return GetTypeDefinitionName(reader, declaring) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetTypeReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetArrayType(string elementType, ArrayShape shape)
    {
        var dims = string.Join(",", shape.Sizes.Zip(shape.LowerBounds, (size, lower) => size + (lower == 0 ? "" : ":" + lower)));
        return elementType + "[" + dims + "]";
    }

    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        "fnptr(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        (isRequired ? "modreq(" : "modopt(") + modifier + "," + unmodifiedType + ")";
    public string GetPinnedType(string elementType) => "pinned(" + elementType + ")";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeDefinitionName(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeReferenceName(reader, handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

internal sealed class ComparisonReport
{
    public required string LeftSha256 { get; init; }
    public required string RightSha256 { get; init; }
    public required string LeftMvid { get; init; }
    public required string RightMvid { get; init; }
    public required string LeftVersion { get; init; }
    public required string RightVersion { get; init; }
    public required int LeftMethods { get; init; }
    public required int RightMethods { get; init; }
    public required int UnchangedMethods { get; init; }
    public required List<string> AddedTypes { get; init; }
    public required List<string> RemovedTypes { get; init; }
    public required List<MethodDelta> AddedMethods { get; init; }
    public required List<MethodDelta> RemovedMethods { get; init; }
    public required List<MethodDelta> ChangedMethods { get; init; }

    public static ComparisonReport Create(AssemblySnapshot left, AssemblySnapshot right)
    {
        var leftTypes = left.Methods.Values.Select(m => m.TypeName).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var rightTypes = right.Methods.Values.Select(m => m.TypeName).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var addedMethods = new List<MethodDelta>();
        var removedMethods = new List<MethodDelta>();
        var changedMethods = new List<MethodDelta>();
        int unchanged = 0;

        foreach (var pair in left.Methods)
        {
            if (!right.Methods.TryGetValue(pair.Key, out MethodSnapshot? r))
            {
                removedMethods.Add(MethodDelta.FromLeft(pair.Key, pair.Value));
                continue;
            }
            if (pair.Value.SemanticSha256 == r.SemanticSha256)
            {
                unchanged++;
                continue;
            }
            changedMethods.Add(new MethodDelta
            {
                Key = pair.Key,
                TypeName = pair.Value.TypeName,
                Name = pair.Value.Name,
                LeftSemanticSha256 = pair.Value.SemanticSha256,
                RightSemanticSha256 = r.SemanticSha256,
                LeftInstructionCount = pair.Value.InstructionCount,
                RightInstructionCount = r.InstructionCount,
                LeftIlSize = pair.Value.IlSize,
                RightIlSize = r.IlSize
            });
        }

        foreach (var pair in right.Methods)
        {
            if (!left.Methods.ContainsKey(pair.Key))
                addedMethods.Add(MethodDelta.FromRight(pair.Key, pair.Value));
        }

        return new ComparisonReport
        {
            LeftSha256 = left.Sha256,
            RightSha256 = right.Sha256,
            LeftMvid = left.Mvid,
            RightMvid = right.Mvid,
            LeftVersion = left.InformationalVersion,
            RightVersion = right.InformationalVersion,
            LeftMethods = left.Methods.Count,
            RightMethods = right.Methods.Count,
            UnchangedMethods = unchanged,
            AddedTypes = rightTypes.Except(leftTypes).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RemovedTypes = leftTypes.Except(rightTypes).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            AddedMethods = addedMethods.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
            RemovedMethods = removedMethods.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
            ChangedMethods = changedMethods.OrderBy(x => x.Key, StringComparer.Ordinal).ToList()
        };
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Managed semantic diff");
        sb.AppendLine();
        sb.AppendLine("| | Left | Right |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| SHA-256 | `" + LeftSha256 + "` | `" + RightSha256 + "` |");
        sb.AppendLine("| MVID | `" + LeftMvid + "` | `" + RightMvid + "` |");
        sb.AppendLine("| Informational version | `" + LeftVersion + "` | `" + RightVersion + "` |");
        sb.AppendLine("| Methods | " + LeftMethods + " | " + RightMethods + " |");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("- Unchanged semantic methods: " + UnchangedMethods);
        sb.AppendLine("- Added methods: " + AddedMethods.Count);
        sb.AppendLine("- Removed methods: " + RemovedMethods.Count);
        sb.AppendLine("- Changed semantic methods: " + ChangedMethods.Count);
        sb.AppendLine("- Added types: " + AddedTypes.Count + (AddedTypes.Count > 0 ? " — " + string.Join(", ", AddedTypes.Take(12)) : ""));
        sb.AppendLine("- Removed types: " + RemovedTypes.Count + (RemovedTypes.Count > 0 ? " — " + string.Join(", ", RemovedTypes.Take(12)) : ""));
        AppendMethodSection(sb, "Added methods", AddedMethods);
        AppendMethodSection(sb, "Removed methods", RemovedMethods);
        AppendMethodSection(sb, "Changed semantic methods", ChangedMethods);
        return sb.ToString();
    }

    private static void AppendMethodSection(StringBuilder sb, string title, List<MethodDelta> methods)
    {
        if (methods.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("## " + title);
        sb.AppendLine();
        foreach (MethodDelta method in methods.Take(200))
            sb.AppendLine("- `" + method.Key + "`");
        if (methods.Count > 200) sb.AppendLine("- … and " + (methods.Count - 200) + " more (see JSON)");
    }
}

internal sealed class MethodDelta
{
    public required string Key { get; init; }
    public required string TypeName { get; init; }
    public required string Name { get; init; }
    public string? LeftSemanticSha256 { get; init; }
    public string? RightSemanticSha256 { get; init; }
    public int LeftInstructionCount { get; init; }
    public int RightInstructionCount { get; init; }
    public int LeftIlSize { get; init; }
    public int RightIlSize { get; init; }

    public static MethodDelta FromLeft(string key, MethodSnapshot s) => new()
    {
        Key = key, TypeName = s.TypeName, Name = s.Name, LeftSemanticSha256 = s.SemanticSha256,
        LeftInstructionCount = s.InstructionCount, LeftIlSize = s.IlSize
    };
    public static MethodDelta FromRight(string key, MethodSnapshot s) => new()
    {
        Key = key, TypeName = s.TypeName, Name = s.Name, RightSemanticSha256 = s.SemanticSha256,
        RightInstructionCount = s.InstructionCount, RightIlSize = s.IlSize
    };
}
