namespace CheatEngine.Mcp.Tests.Support;

internal static class ToolResultAssert
{
	public static void IsSuccess(object result)
	{
		Assert.True(GetProperty<bool>(result, "success"));
	}

	public static void IsFailure(object result, string expectedError)
	{
		Assert.False(GetProperty<bool>(result, "success"));
		Assert.Equal(expectedError, GetProperty<string>(result, "error"));
	}

	public static void HasPropertyValue<T>(object result, string propertyName, T expected)
	{
		Assert.Equal(expected, GetProperty<T>(result, propertyName));
	}

	public static T GetProperty<T>(object result, string propertyName)
	{
		object? value = result.GetType().GetProperty(propertyName)?.GetValue(result);
		return Assert.IsAssignableFrom<T>(value);
	}
}
