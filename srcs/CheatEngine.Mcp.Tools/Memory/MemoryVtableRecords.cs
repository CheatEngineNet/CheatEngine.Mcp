using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>What <c>memory_read_vtable</c> read from one explicitly supplied object or vtable address.</summary>
/// <param name="Address">The resolved address passed to the tool.</param>
/// <param name="VtableAddress">The vtable address read from the object, or <paramref name="Address" /> when it was a direct vtable address.</param>
/// <param name="PointerBytes">The target pointer width in bytes.</param>
/// <param name="StartIndex">The index of the first returned vtable slot.</param>
/// <param name="Slots">The requested slots in index order; an unreadable slot has an error and does not stop later slots.</param>
public sealed record MemoryVtableResult(
	[property: Description("The resolved object or direct vtable address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The vtable address, uppercase hexadecimal without 0x. For an object this is the first pointer stored at address.")]
	string VtableAddress,
	[property: Description("The target pointer width used for the object pointer and every vtable slot: 4 for x86 or 8 for x64.")]
	int PointerBytes,
	[property: Description("The zero-based index of the first returned vtable slot.")]
	int StartIndex,
	[property: Description("The requested vtable slots in ascending index order. This fixed request does not infer the table's extent.")]
	MemoryVtableSlot[] Slots);

/// <summary>One read of a vtable function-pointer slot.</summary>
/// <param name="Index">The zero-based vtable index.</param>
/// <param name="SlotAddress">The address where the function pointer is stored.</param>
/// <param name="FunctionAddress">The pointer value stored in the slot, including 0 for a null slot.</param>
/// <param name="Symbol">Cheat Engine's best-effort name for a non-null function address.</param>
/// <param name="Error">Why this slot could not be read; later slots are still attempted.</param>
public sealed record MemoryVtableSlot(
	[property: Description("The zero-based vtable index.")]
	int Index,
	[property: Description("The slot's address, uppercase hexadecimal without 0x.")]
	string SlotAddress,
	[property: Description("The function pointer stored in the slot, uppercase hexadecimal without 0x; null only when this slot was not read.")]
	string? FunctionAddress = null,
	[property: Description("Cheat Engine's best-effort symbol for a non-null function address; omitted when unavailable.")]
	string? Symbol = null,
	[property: Description("Why this slot could not be read; the other slots are unaffected.")]
	MemoryItemError? Error = null);
