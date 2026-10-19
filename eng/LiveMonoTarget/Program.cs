using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CheatEngine.Mcp.MonoFixture
{
    [StructLayout(LayoutKind.Sequential)]
    public sealed class MonoPrimitiveProbe
    {
        public int Magic = 314159;
        public long Counter = 7000L;
        public double Speed = 1.25d;

        public static MonoPrimitiveProbe StaticRoot;
        public static int StaticMagic = 424242;

        public static int StaticAdd42(int value)
        {
            return checked(value + 42);
        }

        public long InstanceBumpCounter(int delta)
        {
            Counter = checked(Counter + delta);
            return Counter;
        }
    }

    internal static class Program
    {
        private const int MaximumLifetimeMilliseconds = 120000;

        [DllImport("mono-2.0-sgen.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mono_gchandle_get_target")]
        private static extern IntPtr MonoGcHandleGetTarget(uint handle);

        private static int Main(string[] arguments)
        {
            string root;
            string nonce;
            if (!TryReadArguments(arguments, out root, out nonce))
            {
                return 2;
            }

            if (Type.GetType("Mono.Runtime") == null || !Environment.Is64BitProcess)
            {
                WriteFailure(root, nonce, "The target requires the owned x64 Mono runtime.");
                return 3;
            }

            try
            {
                MonoPrimitiveProbe.StaticRoot = new MonoPrimitiveProbe();
                GCHandle handle = GCHandle.Alloc(MonoPrimitiveProbe.StaticRoot, GCHandleType.Pinned);
                try
                {
                    uint handleToken = unchecked((uint)GCHandle.ToIntPtr(handle).ToInt64());
                    IntPtr objectPointer = MonoGcHandleGetTarget(handleToken);
                    if (objectPointer == IntPtr.Zero)
                    {
                        WriteFailure(root, nonce, "mono_gchandle_get_target returned a null MonoObject pointer.");
                        return 4;
                    }

                    // Fixed non-mutating warmup only. The harness owns the sole instance invocation from 7000 to 7007.
                    int staticAdd42WarmupResult = MonoPrimitiveProbe.StaticAdd42(0);
                    if (staticAdd42WarmupResult != 42 || MonoPrimitiveProbe.StaticRoot.Counter != 7000L)
                    {
                        WriteFailure(root, nonce, "The fixed Mono probe initialized to unexpected values.");
                        return 5;
                    }

                    int processId;
                    using (Process current = Process.GetCurrentProcess())
                    {
                        processId = current.Id;
                    }
                    string assemblyPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                    string stopMarker = Path.Combine(root, "mono-owned-" + nonce + ".stop");
                    string manifestPath = Path.Combine(root, "mono-owned-" + nonce + ".manifest.json");
                    WriteManifestAtomically(manifestPath, root, nonce, stopMarker, processId, assemblyPath, handleToken,
                        objectPointer, staticAdd42WarmupResult);

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(MaximumLifetimeMilliseconds);
                    while (DateTime.UtcNow < deadline && !File.Exists(stopMarker))
                    {
                        Thread.Sleep(50);
                    }

                    GC.KeepAlive(MonoPrimitiveProbe.StaticRoot);
                    return 0;
                }
                finally
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                    MonoPrimitiveProbe.StaticRoot = null;
                }
            }
            catch (Exception exception)
            {
                WriteFailure(root, nonce, exception.GetType().FullName + ": " + exception.Message);
                Console.Error.WriteLine(exception);
                return 6;
            }
        }

        private static bool TryReadArguments(string[] arguments, out string root, out string nonce)
        {
            root = null;
            nonce = null;
            if (arguments == null || arguments.Length != 4 || !string.Equals(arguments[0], "--root", StringComparison.Ordinal) ||
                !string.Equals(arguments[2], "--nonce", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                root = Path.GetFullPath(arguments[1]);
            }
            catch (Exception)
            {
                return false;
            }

            nonce = arguments[3];
            if (!Path.IsPathRooted(root) || !Directory.Exists(root) || nonce == null || nonce.Length != 32)
            {
                return false;
            }

            for (int index = 0; index < nonce.Length; index++)
            {
                char value = nonce[index];
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static void WriteManifestAtomically(string manifestPath, string root, string nonce, string stopMarker,
            int processId, string assemblyPath, uint handleToken, IntPtr objectPointer, int staticAdd42WarmupResult)
        {
            string temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string json = "{\n" +
                "  \"nonce\": \"" + Json(nonce) + "\",\n" +
                "  \"pid\": " + processId.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"architecture\": \"x64\",\n" +
                "  \"monoRuntimeMarker\": \"Mono.Runtime\",\n" +
                "  \"ownedRoot\": \"" + Json(root) + "\",\n" +
                "  \"stopMarker\": \"" + Json(stopMarker) + "\",\n" +
                "  \"maximumLifetimeMilliseconds\": " + MaximumLifetimeMilliseconds.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"assemblyFullPath\": \"" + Json(assemblyPath) + "\",\n" +
                "  \"assemblySha256\": \"" + Hash(assemblyPath) + "\",\n" +
                "  \"namespace\": \"CheatEngine.Mcp.MonoFixture\",\n" +
                "  \"class\": \"MonoPrimitiveProbe\",\n" +
                "  \"objectPointer\": \"" + objectPointer.ToInt64().ToString("X", CultureInfo.InvariantCulture) + "\",\n" +
                "  \"gcHandleToken\": " + handleToken.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"fields\": { \"Magic\": 314159, \"Counter\": 7000, \"Speed\": 1.25 },\n" +
                "  \"staticMagic\": 424242,\n" +
                "  \"staticAdd42WarmupResult\": " + staticAdd42WarmupResult.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"expectedInstanceBumpArgument\": 7,\n" +
                "  \"expectedCounterAfterInvoke\": 7007\n" +
                "}\n";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, manifestPath);
        }

        private static void WriteFailure(string root, string nonce, string detail)
        {
            try
            {
                File.WriteAllText(Path.Combine(root, "mono-owned-" + nonce + ".failure.txt"), detail, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Startup failure reporting must never obscure the original bounded exit code.
            }
        }

        private static string Hash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(stream);
                StringBuilder value = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    value.Append(item.ToString("X2", CultureInfo.InvariantCulture));
                }
                return value.ToString();
            }
        }

        private static string Json(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
