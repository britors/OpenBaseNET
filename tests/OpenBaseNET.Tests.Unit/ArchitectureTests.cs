using System.Reflection;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Tests.Unit;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_depends_only_on_standard_dotnet_libraries()
    {
        Assert.All(typeof(Customer).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(IsStandardLibrary(reference.Name!), $"Domain depends on {reference.Name}."));
    }

    [Fact]
    public void Application_depends_only_on_domain_and_standard_dotnet_libraries()
    {
        Assert.All(typeof(CreateCustomer).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name == typeof(Customer).Assembly.GetName().Name || IsStandardLibrary(reference.Name!),
                $"Application depends on {reference.Name}."));
    }

    [Fact]
    public void Port_signatures_do_not_expose_provider_or_query_framework_types()
    {
        var ports = typeof(ICustomerRepository).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(ICustomerRepository).Namespace).ToArray();
        Assert.NotEmpty(ports);
        foreach (var port in ports)
        {
            foreach (var method in port.GetMethods())
            {
                CheckType(method.ReturnType);
                foreach (var parameter in method.GetParameters()) CheckType(parameter.ParameterType);
                foreach (var argument in method.GetGenericArguments())
                    foreach (var constraint in argument.GetGenericParameterConstraints()) CheckType(constraint);
            }
            foreach (var property in port.GetProperties()) CheckType(property.PropertyType);
        }
    }

    private static void CheckType(Type type)
    {
        if (type.IsGenericParameter) return;
        if (type.HasElementType) CheckType(type.GetElementType()!);
        var name = type.Namespace ?? "";
        Assert.False(name.StartsWith("System.Data", StringComparison.Ordinal), $"Port leaks {type}.");
        Assert.False(name.StartsWith("System.Linq", StringComparison.Ordinal), $"Port leaks {type}.");
        var assembly = type.Assembly.GetName().Name!;
        Assert.True(IsStandardLibrary(assembly) || assembly is "OpenBaseNET.Domain" or "OpenBaseNET.Application",
            $"Port leaks {type} from {assembly}.");
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments()) CheckType(argument);
    }

    private static bool IsStandardLibrary(string name) =>
        name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard";
}
