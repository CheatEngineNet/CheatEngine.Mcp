// Fixed, owned x64 symbol fixture. It has no DllMain, threads, imports beyond the CRT, or target-side side effects on load.
#include <cstddef>
#include <cstdint>

struct CemcpPdbFixtureLayout
{
	std::uint32_t Magic;
	std::int16_t Delta;
	std::uint16_t Flags;
	std::uint64_t Counter;
};

static_assert(sizeof(CemcpPdbFixtureLayout) == 16);
static_assert(offsetof(CemcpPdbFixtureLayout, Magic) == 0);
static_assert(offsetof(CemcpPdbFixtureLayout, Delta) == 4);
static_assert(offsetof(CemcpPdbFixtureLayout, Flags) == 6);
static_assert(offsetof(CemcpPdbFixtureLayout, Counter) == 8);

extern "C" CemcpPdbFixtureLayout CemcpFixtureKnownLayout =
	{ 0x4D435046u, static_cast<std::int16_t>(-17), 0x2A5Cu, 41u };

extern "C" __declspec(dllexport) std::uint32_t CemcpFixtureLayoutSize() noexcept
{
	return static_cast<std::uint32_t>(sizeof(CemcpPdbFixtureLayout));
}

// Reserved for a later owned debugger-loop fixture; injection never calls it.
extern "C" __declspec(dllexport) std::uint64_t CemcpFixtureTick(CemcpPdbFixtureLayout* value) noexcept
{
	if (value == nullptr)
	{
		return 0;
	}
	return ++value->Counter;
}

