// Empty placeholder so MSBuild has a compilation unit. The compiled
// ClientDeps.dll is discarded by Build-Release.ps1 - only the transitively
// copied NuGet dependency DLLs are kept in 1.6/Assemblies/.
namespace ClientDeps
{
    internal static class Placeholder { }
}
