using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class McpValueCodecTests
{
	private static readonly Address Target = new(0x1000);

	[Theory]
	[InlineData(McpValueType.Int8, 8, 1)]
	[InlineData(McpValueType.UInt16, 8, 2)]
	[InlineData(McpValueType.Float, 8, 4)]
	[InlineData(McpValueType.UInt64, 4, 8)]
	[InlineData(McpValueType.Pointer, 4, 4)]
	[InlineData(McpValueType.Pointer, 8, 8)]
	public void FixedSize_FixedType_IsItsWidth(McpValueType type, int pointerSize, int expected)
	{
		Assert.Equal(expected, McpValueCodec.FixedSize(type, pointerSize));
	}

	[Theory]
	[InlineData(McpValueType.String)]
	[InlineData(McpValueType.WString)]
	[InlineData(McpValueType.Bytes)]
	public void FixedSize_VariableType_IsNull(McpValueType type)
	{
		Assert.Null(McpValueCodec.FixedSize(type, 8));
	}

	[Fact]
	public void Format_Primitives_UseInvariantContractText()
	{
		Assert.Equal("-128", McpValueCodec.Format(sbyte.MinValue));
		Assert.Equal("18446744073709551615", McpValueCodec.Format(ulong.MaxValue));
		Assert.Equal("0.1", McpValueCodec.Format(0.1f));
		Assert.Equal("0.1", McpValueCodec.Format(0.1d));
		Assert.Equal("NaN", McpValueCodec.Format(float.NaN));
		Assert.Equal("Infinity", McpValueCodec.Format(double.PositiveInfinity));
		Assert.Equal("-Infinity", McpValueCodec.Format(float.NegativeInfinity));
		Assert.Equal("7FF6A1B2C3D0", McpValueCodec.Format(new Address(0x7FF6A1B2C3D0)));
	}

	[Theory]
	[InlineData(McpValueType.Int8, "-1", "FF")]
	[InlineData(McpValueType.Int8, "0xFF", "FF")]
	[InlineData(McpValueType.Int8, "-128", "80")]
	[InlineData(McpValueType.UInt8, "255", "FF")]
	[InlineData(McpValueType.Int16, "-2", "FE FF")]
	[InlineData(McpValueType.UInt16, "0x1234", "34 12")]
	[InlineData(McpValueType.Int32, "-0x10", "F0 FF FF FF")]
	[InlineData(McpValueType.UInt32, "4294967295", "FF FF FF FF")]
	[InlineData(McpValueType.Int64, "-9223372036854775808", "00 00 00 00 00 00 00 80")]
	[InlineData(McpValueType.UInt64, "0xFFFFFFFFFFFFFFFF", "FF FF FF FF FF FF FF FF")]
	[InlineData(McpValueType.Float, "1.5", "00 00 C0 3F")]
	[InlineData(McpValueType.Float, "-Infinity", "00 00 80 FF")]
	[InlineData(McpValueType.Double, "NaN", "00 00 00 00 00 00 F8 FF")]
	[InlineData(McpValueType.Pointer, "7FF6A1B2C3D0", "D0 C3 B2 A1 F6 7F 00 00")]
	[InlineData(McpValueType.String, "hé", "68 C3 A9")]
	[InlineData(McpValueType.WString, "hé", "68 00 E9 00")]
	[InlineData(McpValueType.Bytes, "488B05", "48 8B 05")]
	public void Encode_ValueText_IsLittleEndianTargetBytes(McpValueType type, string value, string expected)
	{
		Assert.Equal(expected, HexFormat.Bytes(McpValueCodec.Encode(type, value, 8, "value")));
	}

	[Theory]
	[InlineData(McpValueType.Int8, "128")]
	[InlineData(McpValueType.Int8, "-129")]
	[InlineData(McpValueType.Int8, "0x100")]
	[InlineData(McpValueType.UInt8, "-1")]
	[InlineData(McpValueType.UInt32, "4294967296")]
	[InlineData(McpValueType.Int32, "1.5")]
	[InlineData(McpValueType.Int32, "12abc")]
	[InlineData(McpValueType.Int64, "")]
	[InlineData(McpValueType.Float, "1e40")]
	[InlineData(McpValueType.Double, "1,5")]
	[InlineData(McpValueType.Pointer, "game.exe+10")]
	public void Encode_InvalidValue_IsInvalidArgumentForTheParameter(McpValueType type, string value)
	{
		ToolError error = Assert.Throws<CheatEngineToolException>(() => McpValueCodec.Encode(type, value, 8, "value"))
			.Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal("""{"parameter":"value"}""", error.Details!.Value.GetRawText());
	}

	[Fact]
	public void Encode_PointerWiderThanATarget32BitPointer_IsInvalid()
	{
		Assert.Equal("78 56 34 12", HexFormat.Bytes(McpValueCodec.Encode(McpValueType.Pointer, "12345678", 4, "v")));
		Assert.Throws<CheatEngineToolException>(() => McpValueCodec.Encode(McpValueType.Pointer, "100000000", 4, "v"));
	}

	[Theory]
	[InlineData(McpValueType.Int8, "SByte", "-5")]
	[InlineData(McpValueType.UInt8, "Byte", "250")]
	[InlineData(McpValueType.Int16, "Int16", "-300")]
	[InlineData(McpValueType.UInt16, "UInt16", "65535")]
	[InlineData(McpValueType.Int32, "Int32", "-70000")]
	[InlineData(McpValueType.UInt32, "UInt32", "4000000000")]
	[InlineData(McpValueType.Int64, "Int64", "-5000000000")]
	[InlineData(McpValueType.UInt64, "UInt64", "18000000000000000000")]
	[InlineData(McpValueType.Float, "Single", "NaN")]
	[InlineData(McpValueType.Double, "Double", "-0.25")]
	[InlineData(McpValueType.Pointer, "Address", "7FF6A1B2C3D0")]
	public void ReadAndWrite_FixedType_UseTheClosedGenericPrimitiveApi(McpValueType type, string clrType,
		string value)
	{
		MemoryDouble memory = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

		McpValueCodec.Write(client, Target, type, value, CancellationToken.None);
		Assert.Equal("WritePrimitive", memory.LastMethod);
		Assert.Equal(clrType, memory.LastGenericType);
		Assert.Equal(Target, memory.LastAddress);

		Assert.Equal(value, McpValueCodec.Read(client, Target, type, null, CancellationToken.None));
		Assert.Equal("ReadPrimitive", memory.LastMethod);
		Assert.Equal(clrType, memory.LastGenericType);
	}

	[Fact]
	public void ReadAndWrite_Strings_UseTheirEncodingAndLength()
	{
		MemoryDouble memory = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

		McpValueCodec.Write(client, Target, McpValueType.String, "hé", CancellationToken.None);
		MemoryStringWriteRequest utf8 = Assert.IsType<MemoryStringWriteRequest>(memory.LastRequest);
		Assert.Equal((MemoryStringEncoding.Utf8, 3, "hé"), (utf8.Encoding, utf8.MaximumLength, utf8.Value));

		McpValueCodec.Write(client, Target, McpValueType.WString, "hé", CancellationToken.None);
		MemoryStringWriteRequest utf16 = Assert.IsType<MemoryStringWriteRequest>(memory.LastRequest);
		Assert.Equal((MemoryStringEncoding.Utf16, 2), (utf16.Encoding, utf16.MaximumLength));

		Assert.Equal("hé", McpValueCodec.Read(client, Target, McpValueType.WString, 32, CancellationToken.None));
		MemoryStringReadRequest read = Assert.IsType<MemoryStringReadRequest>(memory.LastRequest);
		Assert.Equal((MemoryStringEncoding.Utf16, 32), (read.Encoding, read.MaximumLength));
		McpValueCodec.Read(client, Target, McpValueType.String, null, CancellationToken.None);
		Assert.Equal(McpValueCodec.DefaultStringLength,
			Assert.IsType<MemoryStringReadRequest>(memory.LastRequest).MaximumLength);
	}

	[Fact]
	public void ReadAndWrite_Bytes_UseSpacedHex()
	{
		MemoryDouble memory = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

		McpValueCodec.Write(client, Target, McpValueType.Bytes, "48 8b 05", CancellationToken.None);
		Assert.Equal("WriteBytes", memory.LastMethod);

		Assert.Equal("48 8B 05", McpValueCodec.Read(client, Target, McpValueType.Bytes, 3, CancellationToken.None));
		Assert.Equal(3, Assert.IsType<MemoryBytesReadRequest>(memory.LastRequest).Length);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(McpValueCodec.MaxLength + 1)]
	public void Read_BytesWithoutAValidLength_IsRefusedBeforeTheClient(int? length)
	{
		MemoryDouble memory = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			McpValueCodec.Read(client, Target, McpValueType.Bytes, length, CancellationToken.None));

		Assert.Equal("""{"parameter":"length"}""", exception.Error.Details!.Value.GetRawText());
		Assert.Null(memory.LastMethod);
	}

	[Fact]
	public void Write_InvalidValue_IsRefusedBeforeTheClient()
	{
		MemoryDouble memory = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

		Assert.Throws<CheatEngineToolException>(() =>
			McpValueCodec.Write(client, Target, McpValueType.UInt8, "256", CancellationToken.None));

		Assert.Null(memory.LastMethod);
	}

	/// <summary>An in-memory typed memory client that stores the last primitive written and returns it on read.</summary>
	private sealed class MemoryDouble
	{
		private object? _stored;

		internal MemoryDouble()
		{
			Client = ClientTestDouble.Create<IMemoryClient>(Handle);
		}

		internal IMemoryClient Client
		{
			get;
		}

		internal string? LastMethod
		{
			get;
			private set;
		}

		internal string? LastGenericType
		{
			get;
			private set;
		}

		internal Address? LastAddress
		{
			get;
			private set;
		}

		internal object? LastRequest
		{
			get;
			private set;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			LastMethod = method.Name;
			LastGenericType = method.IsGenericMethod ? method.GetGenericArguments()[0].Name : null;
			switch (method.Name)
			{
				case "WritePrimitive":
					LastAddress = (Address) arguments![0]!;
					_stored = arguments[1];
					return null;
				case "ReadPrimitive":
					LastAddress = (Address) arguments![0]!;
					return _stored;
				case "WriteString" or "WriteBytes":
					LastRequest = arguments![0];
					return null;
				case "ReadString":
					LastRequest = arguments![0];
					return "hé";
				case "ReadBytes":
					LastRequest = arguments![0];
					return ImmutableArray.Create<byte>(0x48, 0x8B, 0x05);
				default:
					throw new XunitException($"Unexpected memory call: {method.Name}.");
			}
		}
	}
}
