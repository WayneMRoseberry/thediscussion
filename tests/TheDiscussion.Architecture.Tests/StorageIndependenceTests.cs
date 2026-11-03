using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using TheDiscussion.Core;

namespace TheDiscussion.Architecture.Tests;

/// <summary>
/// AC-1: the core and API projects must not depend on any storage provider project or storage library.
/// The only exception is the API's composition root (namespace <see cref="CompositionRootNamespace"/>),
/// which picks the provider from configuration.
/// </summary>
public class StorageIndependenceTests
{
    private const string CompositionRootNamespace = "TheDiscussion.Api.Composition";

    /// <summary>Namespace / assembly-name prefixes that belong to storage providers or storage libraries.</summary>
    private static readonly string[] ForbiddenPrefixes =
    [
        "TheDiscussion.Storage",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
    ];

    private static readonly Assembly CoreAssemblyRef = typeof(CoreAssembly).Assembly;
    private static readonly Assembly ApiAssemblyRef = typeof(TheDiscussion.Api.DiscussionApi).Assembly;

    [Fact]
    public void Core_assembly_does_not_reference_storage_assemblies()
    {
        var offending = CoreAssemblyRef.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbidden)
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Core_types_do_not_depend_on_storage_namespaces()
    {
        var result = Types.InAssembly(CoreAssemblyRef)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenPrefixes)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Api_types_outside_the_composition_root_do_not_depend_on_storage_namespaces()
    {
        var result = Types.InAssembly(ApiAssemblyRef)
            .That()
            .DoNotResideInNamespace(CompositionRootNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenPrefixes)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Core_project_file_has_no_storage_references()
    {
        var references = ProjectReferences("src/TheDiscussion.Core/TheDiscussion.Core.csproj");

        Assert.DoesNotContain(references, reference => IsForbidden(Path.GetFileNameWithoutExtension(reference)));
    }

    [Fact]
    public void Api_project_file_has_no_direct_storage_library_packages()
    {
        // The API project may reference provider projects (for its composition root), but never
        // EF Core or Npgsql packages directly: those belong to the PostgreSQL provider project.
        var packages = PackageReferences("src/TheDiscussion.Api/TheDiscussion.Api.csproj");

        Assert.DoesNotContain(packages, IsForbidden);
    }

    private static bool IsForbidden(string name) =>
        ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Types with forbidden storage dependencies: " + string.Join(", ", result.FailingTypeNames ?? []);

    private static List<string> ProjectReferences(string relativePath) =>
        LoadProject(relativePath).Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .Concat(PackageReferences(relativePath))
            .ToList();

    private static List<string> PackageReferences(string relativePath) =>
        LoadProject(relativePath).Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();

    private static XDocument LoadProject(string relativePath) =>
        XDocument.Load(Path.Combine(RepositoryRoot.Path, relativePath));
}
