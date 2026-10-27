#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

struct lua_State;
using LuaFunction = int(__cdecl*)(lua_State*);
using PushInteger = void(__cdecl*)(lua_State*, long long);
using CreateTable = void(__cdecl*)(lua_State*, int, int);
using PushClosure = void(__cdecl*)(lua_State*, LuaFunction, int);
using SetField = void(__cdecl*)(lua_State*, int, const char*);

// Registration only resolves the already loaded Lua API; it never opens a socket.
static HMODULE LuaModule() { return GetModuleHandleW(L"lua53-64.dll"); }

static int ConnectError(int family, const char* address)
{
	WSADATA data{};
	const int started = WSAStartup(MAKEWORD(2, 2), &data);
	if (started != 0) return started;
	SOCKET connection = socket(family, SOCK_STREAM, IPPROTO_TCP);
	int error = connection == INVALID_SOCKET ? WSAGetLastError() : 0;
	if (connection != INVALID_SOCKET)
	{
		u_long nonblocking = 1;
		if (ioctlsocket(connection, FIONBIO, &nonblocking) != 0) error = WSAGetLastError();
		else
		{
			int connected;
			if (family == AF_INET)
			{
				sockaddr_in endpoint{};
				endpoint.sin_family = AF_INET;
				endpoint.sin_port = htons(9);
				InetPtonA(AF_INET, address, &endpoint.sin_addr);
				connected = connect(connection, reinterpret_cast<const sockaddr*>(&endpoint), sizeof(endpoint));
			}
			else
			{
				sockaddr_in6 endpoint{};
				endpoint.sin6_family = AF_INET6;
				endpoint.sin6_port = htons(9);
				InetPtonA(AF_INET6, address, &endpoint.sin6_addr);
				connected = connect(connection, reinterpret_cast<const sockaddr*>(&endpoint), sizeof(endpoint));
			}
			error = connected == 0 ? 0 : WSAGetLastError();
			if (error == WSAEWOULDBLOCK)
			{
				fd_set writeSet, errorSet;
				FD_ZERO(&writeSet); FD_ZERO(&errorSet);
				FD_SET(connection, &writeSet); FD_SET(connection, &errorSet);
				timeval timeout{1, 0};
				const int ready = select(0, nullptr, &writeSet, &errorSet, &timeout);
				if (ready == SOCKET_ERROR) error = WSAGetLastError();
				else if (ready == 0) error = WSAETIMEDOUT;
				else
				{
					int length = sizeof(error);
					if (getsockopt(connection, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&error), &length) != 0)
						error = WSAGetLastError();
				}
			}
		}
		closesocket(connection);
	}
	WSACleanup();
	return error;
}

static int Probe(lua_State* state, int family, const char* address)
{
	HMODULE module = LuaModule();
	if (module == nullptr) return 0;
	auto pushInteger = reinterpret_cast<PushInteger>(GetProcAddress(module, "lua_pushinteger"));
	if (pushInteger == nullptr) return 0;
	pushInteger(state, ConnectError(family, address));
	return 1;
}

static int Ipv4(lua_State* state) { return Probe(state, AF_INET, "192.0.2.1"); }
static int Ipv6(lua_State* state) { return Probe(state, AF_INET6, "2001:db8::1"); }

static int CurrentPid(lua_State* state)
{
	HMODULE module = LuaModule();
	if (module == nullptr) return 0;
	auto pushInteger = reinterpret_cast<PushInteger>(GetProcAddress(module, "lua_pushinteger"));
	if (pushInteger == nullptr) return 0;
	pushInteger(state, static_cast<long long>(GetCurrentProcessId()));
	return 1;
}

extern "C" __declspec(dllexport) int luaopen_cemcpnetworkprobe(lua_State* state)
{
	HMODULE module = LuaModule();
	if (module == nullptr) return 0;
	auto createTable = reinterpret_cast<CreateTable>(GetProcAddress(module, "lua_createtable"));
	auto pushClosure = reinterpret_cast<PushClosure>(GetProcAddress(module, "lua_pushcclosure"));
	auto setField = reinterpret_cast<SetField>(GetProcAddress(module, "lua_setfield"));
	if (createTable == nullptr || pushClosure == nullptr || setField == nullptr) return 0;
	createTable(state, 0, 3);
	pushClosure(state, Ipv4, 0); setField(state, -2, "ipv4");
	pushClosure(state, Ipv6, 0); setField(state, -2, "ipv6");
	pushClosure(state, CurrentPid, 0); setField(state, -2, "pid");
	return 1;
}
