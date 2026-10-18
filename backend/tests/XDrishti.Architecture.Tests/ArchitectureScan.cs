using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Mono.Cecil;

namespace XDrishti.Architecture.Tests;

/// <summary>The checkouts and project files the architecture rules read: where a file of the repository is, which projects a project file references, what is build output.</summary>
internal static class RepositoryFiles
{
    public static bool IsBuildOutput(string file) => file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    public static bool IsUnder(string file, string directory) => file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public static List<string> ProjectReferences(XDocument project) =>
        [.. project.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/')))];

    /// <summary>Finds a file of the repository by walking up from the test binaries (the tests run from a checkout, never from a published image).</summary>
    public static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The file " + string.Join('/', parts) + " was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Whether the source text calls the compiler warm-up of the Strategies library.</summary>
    public static bool CallsTheWarmUp(string source) => source.Contains("CompilerWarmup.Run", StringComparison.Ordinal);
}

/// <summary>Reads the member and type references recorded in an assembly's metadata, and finds static fields that can change (shared by the rules of the pure libraries).</summary>
internal static class AssemblyScan
{
    public static List<(string Type, string Member)> MemberReferences(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var found = new List<(string Type, string Member)>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), reader.GetString(member.Name)));
            }
        }

        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), string.Empty));
        }

        return found;
    }

    /// <summary>
    /// The methods and fields that the code of one type (its methods, its closures and the state machines of its async methods) calls or reads, as (declaring type, member name), read from the IL. Unlike
    /// <see cref="MemberReferences"/>, which lists what a whole assembly references, this one can say that <c>this</c> type reads the clock.
    /// </summary>
    public static List<(string Type, string Member)> MembersUsedBy(Assembly assembly, string typeFullName)
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
        var root = module.GetType(typeFullName.Replace('+', '/')) ?? throw new InvalidOperationException("The type " + typeFullName + " is not in " + assembly.GetName().Name);
        var used = new List<(string Type, string Member)>();
        var pending = new Stack<Mono.Cecil.TypeDefinition>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            foreach (var nested in type.NestedTypes)
            {
                pending.Push(nested);
            }

            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference call:
                            used.Add((call.DeclaringType.FullName, call.Name));
                            break;
                        case FieldReference field:
                            used.Add((field.DeclaringType.FullName, field.Name));
                            break;
                    }
                }
            }
        }

        return used;
    }

    /// <summary>The names of the members of <c>FeatureFrame</c> the assembly references (properties as their accessors, <c>get_Steps</c>), each once.</summary>
    public static List<string> FrameMembersUsed(Assembly assembly) =>
        [.. MemberReferences(assembly).Where(c => c.Type == "XDrishti.Indicators.FeatureFrame" && c.Member.Length > 0).Select(c => c.Member).Distinct()];

    public static List<string> MutableStatics(Assembly assembly)
    {
        var offenders = new List<string>();
        foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                {
                    offenders.Add(type.FullName + "." + field.Name);
                }
            }
        }

        return offenders;
    }
}
