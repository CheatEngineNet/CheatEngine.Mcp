using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveDotNetMetadataQualificationTests
{
	[Fact]
	public void ReadExpectation_BuiltOwnedTarget_MatchesReflectedTypeAndMethodIdentity()
	{
		string configuration = new FileInfo(typeof(LiveDotNetMetadataQualificationTests).Assembly.Location).Directory!.Name;
		string path = Path.Combine(RepositoryPaths.Root, "artifacts", "bin", "CheatEngine.Mcp.LiveTarget", configuration,
			"CheatEngine.Mcp.LiveTarget.dll");
		LiveDotNetMetadataQualification.AssemblyExpectation expected = LiveDotNetMetadataQualification.ReadExpectation(path);
		AssemblyLoadContext context = new("live-target-metadata-oracle", isCollectible: true);
		try
		{
			Assembly assembly = context.LoadFromAssemblyPath(path);
			Type program = assembly.GetType("CheatEngine.Mcp.LiveTarget.Program", throwOnError: true)!;
			FieldInfo field = program.GetField("PointerOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
			MethodInfo method = program.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
			Assert.Equal("CheatEngine.Mcp.LiveTarget.dll", expected.FileName);
			Assert.Equal(program.FullName, expected.TypeName);
			Assert.Equal(program.MetadataToken.ToString(CultureInfo.InvariantCulture), expected.TypeToken);
			Assert.Equal(field.Name, expected.FieldName);
			Assert.Equal(method.Name, expected.MethodName);
			Assert.Equal(method.MetadataToken.ToString(CultureInfo.InvariantCulture), expected.MethodToken);
			Assert.Equal(["args"], expected.ParameterNames);
			Assert.Equal(method.GetParameters().Select(parameter => parameter.Name), expected.ParameterNames);
			Type probe = assembly.GetType("CheatEngine.Mcp.LiveTarget.ManagedObjectProbe", throwOnError: true)!;
			FieldInfo[] probeFields = probe.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			Assert.Equal(["Marker", "Mirror", "Toggle"], probeFields.Select(field => field.Name));
			Assert.All(probeFields, field => Assert.Equal(typeof(int), field.FieldType));
			Assert.Empty(probe.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
		}
		finally
		{
			context.Unload();
		}
	}
}
