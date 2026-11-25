using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Components;

/// <summary>
/// Reads component types from compiled assemblies with metadata only. The
/// assembly is never loaded into the CLI process, so inspecting a package cannot
/// run its module initializers or other code.
/// </summary>
public static class MetadataComponentReader
{
    /// <summary>Reads components from every resolved assembly that contains them.</summary>
    public static List<ComponentMetadata> ReadProject(DonkehProject project)
    {
        var components = new List<ComponentMetadata>();

        foreach (var assembly in ProjectDependencyResolver.ResolveAssemblies(project))
        {
            try
            {
                components.AddRange(ReadAssembly(assembly.Path, assembly.PackageId));
            }
            catch (Exception)
            {
                // Native or reference assemblies can fail to parse; they are not
                // component libraries we can inspect.
            }
        }

        return components;
    }

    /// <summary>Reads component types from one assembly path.</summary>
    public static List<ComponentMetadata> ReadAssembly(string assemblyPath, string? packageId = null)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            return [];
        }

        var reader = peReader.GetMetadataReader();
        var provider = new TypeNameProvider();
        var types = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        var simpleTypes = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        var docs = ReadXmlDocs(assemblyPath);
        var assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(assemblyPath);

        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            var namespaceName = reader.GetString(definition.Namespace);
            var name = reader.GetString(definition.Name);
            var fullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
            types[fullName] = handle;
            simpleTypes.TryAdd(name, handle);
        }

        var result = new List<ComponentMetadata>();

        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);

            if (name.StartsWith("<", StringComparison.Ordinal) || name is "<Module>")
            {
                continue;
            }

            var visibility = definition.Attributes & TypeAttributes.VisibilityMask;

            if (visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
            {
                continue;
            }

            var namespaceName = reader.GetString(definition.Namespace);
            var baseType = TryTypeName(reader, definition.BaseType, provider) ?? "";

            if (!IsComponentBase(baseType))
            {
                continue;
            }

            var propsType = PropsTypeName(baseType);
            var propsHandle = propsType is null ? default : FindType(types, simpleTypes, propsType);
            var properties = propsHandle.IsNil
                ? []
                : ReadProperties(reader, propsHandle, provider, docs);
            var events = properties.Where(property => property.IsEvent).Select(property => new ComponentEvent
            {
                Name = property.Name,
                Type = property.Type,
                EventArgsType = EventArgumentType(property.Type),
                Description = property.Description
            }).ToList();
            var componentFullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
            var description = LookupDoc(docs, $"T:{componentFullName}")
                ?? $"Component {name} from {assemblyName}.";
            var lifecycle = ReadLifecycle(reader, definition);
            var isAbstract = (definition.Attributes & TypeAttributes.Abstract) != 0;

            result.Add(new ComponentMetadata
            {
                Name = name,
                Namespace = namespaceName,
                Package = packageId ?? assemblyName,
                Source = assemblyPath,
                Kind = "assembly",
                BaseType = baseType,
                Description = description,
                PropsType = propsType,
                Properties = properties.Where(property => !property.IsEvent).ToList(),
                Events = events,
                SupportsChildren = SupportsChildren(reader, propsHandle, provider),
                SupportsState = false,
                Lifecycle = lifecycle,
                Dependencies = [assemblyName],
                Examples =
                [
                    propsType is null
                        ? $"C<{name}>(new())"
                        : $"C<{name}, {propsType}>(new {propsType}())"
                ],
                Tags = Tags(name, namespaceName, description, propsType),
                IsAbstract = isAbstract
            });
        }

        return result;
    }

    private static bool IsComponentBase(string baseType)
    {
        var name = StripGeneric(baseType);
        return name is "Component" or "Donkeh.UILab.Component"
            || baseType.Contains(".Component<", StringComparison.Ordinal)
            || baseType.StartsWith("Component<", StringComparison.Ordinal);
    }

    private static string StripGeneric(string type)
    {
        var index = type.IndexOf('<');
        return (index > 0 ? type[..index] : type).Trim();
    }

    private static string? PropsTypeName(string baseType)
    {
        var match = Regex.Match(baseType, @"Component\s*<\s*(?<props>[^,>]+)");
        return match.Success ? match.Groups["props"].Value.Trim() : null;
    }

    private static TypeDefinitionHandle FindType(
        Dictionary<string, TypeDefinitionHandle> types,
        Dictionary<string, TypeDefinitionHandle> simpleTypes,
        string name)
    {
        if (types.TryGetValue(name, out var exact))
        {
            return exact;
        }

        var simple = name.Split('.').Last();
        return simpleTypes.TryGetValue(simple, out var handle) ? handle : default;
    }

    private static string? TryTypeName(
        MetadataReader reader,
        EntityHandle handle,
        TypeNameProvider provider)
    {
        if (handle.IsNil)
        {
            return null;
        }

        return handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(provider, null),
            _ => null
        };
    }

    private static List<ComponentProperty> ReadProperties(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        TypeNameProvider provider,
        Dictionary<string, string> docs)
    {
        var definition = reader.GetTypeDefinition(handle);
        var namespaceName = reader.GetString(definition.Namespace);
        var typeName = reader.GetString(definition.Name);
        var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
        var result = new List<ComponentProperty>();

        foreach (var propertyHandle in definition.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var name = reader.GetString(property.Name);
            var signature = property.DecodeSignature(provider, null);
            var type = signature.ReturnType;
            var accessors = property.GetAccessors();
            var isPublic = (!accessors.Getter.IsNil && IsPublic(reader, accessors.Getter))
                || (!accessors.Setter.IsNil && IsPublic(reader, accessors.Setter));

            if (!isPublic)
            {
                continue;
            }

            var isEvent = name.StartsWith("On", StringComparison.Ordinal)
                && (type.Contains("Action", StringComparison.Ordinal)
                    || type.Contains("Func", StringComparison.Ordinal)
                    || type.Contains("Delegate", StringComparison.Ordinal));

            result.Add(new ComponentProperty
            {
                Name = name,
                Type = type,
                Description = LookupDoc(docs, $"P:{fullName}.{name}"),
                IsEvent = isEvent
            });
        }

        return result;
    }

    private static bool IsPublic(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        var access = method.Attributes & MethodAttributes.MemberAccessMask;
        return access == MethodAttributes.Public;
    }

    private static bool SupportsChildren(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        TypeNameProvider provider)
    {
        if (handle.IsNil)
        {
            return false;
        }

        var definition = reader.GetTypeDefinition(handle);
        var baseType = TryTypeName(reader, definition.BaseType, provider) ?? "";

        if (baseType.Contains("PropsWithChildren", StringComparison.Ordinal))
        {
            return true;
        }

        return definition.GetProperties().Any(propertyHandle =>
            reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name)
                .Equals("Children", StringComparison.Ordinal));
    }

    private static List<string> ReadLifecycle(MetadataReader reader, TypeDefinition definition)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var methodHandle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);

            if (name is "OnMount" or "OnUpdate" or "OnUnmount" or "ShouldRender"
                && (method.Attributes & MethodAttributes.Virtual) != 0)
            {
                names.Add(name);
            }
        }

        return names.ToList();
    }

    private static string? EventArgumentType(string type)
    {
        var match = Regex.Match(type, @"(?:Action|Func)\s*<\s*(?<args>[^>]+)>");

        if (!match.Success)
        {
            return null;
        }

        var args = match.Groups["args"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return args.Length == 0 ? null : args[^1];
    }

    private static List<string> Tags(
        string name,
        string? namespaceName,
        string? description,
        string? propsType)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in SourceScanner.Words(name))
        {
            tags.Add(word);
        }

        if (namespaceName is not null)
        {
            foreach (var part in namespaceName.Split('.'))
            {
                foreach (var word in SourceScanner.Words(part))
                {
                    tags.Add(word);
                }
            }
        }

        if (propsType is not null)
        {
            foreach (var word in SourceScanner.Words(propsType.Replace("Props", "", StringComparison.Ordinal)))
            {
                tags.Add(word);
            }
        }

        if (description is not null)
        {
            foreach (var word in description.Split(
                [' ', ',', '.', ';', ':', '(', ')', '[', ']', '/', '\\', '-', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (word.Length > 2)
                {
                    tags.Add(word.ToLowerInvariant());
                }
            }
        }

        return tags.ToList();
    }

    private static Dictionary<string, string> ReadXmlDocs(string assemblyPath)
    {
        var docs = new Dictionary<string, string>(StringComparer.Ordinal);
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");

        if (!File.Exists(xmlPath))
        {
            return docs;
        }

        try
        {
            var document = XDocument.Load(xmlPath);

            foreach (var member in document.Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var summary = member.Element("summary");

                if (summary is not null)
                {
                    docs[name] = SourceScanner.CleanDoc(summary.Value) ?? "";
                }
            }
        }
        catch (Exception)
        {
            // Missing or malformed XML docs only remove descriptions.
        }

        return docs;
    }

    private static string? LookupDoc(Dictionary<string, string> docs, string key) =>
        docs.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    /// <summary>Resolves metadata type handles to readable C# names.</summary>
    private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.UIntPtr => "nuint",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var namespaceName = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
        }

        public string GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            var namespaceName = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

        public string GetByReferenceType(string elementType) => "ref " + elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    }
}
