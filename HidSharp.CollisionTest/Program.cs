using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace HidSharp.CollisionTest
{
    // Loads one or more copies of HidSharp ("stock" = 2.1.0 from NuGet, "patched" = built from
    // this repository) into separate AssemblyLoadContexts of ONE process, then enumerates HID
    // devices through each copy. Every copy starts its own device-monitor window, so two copies
    // that use the same window class name collide, which is the Logi Plugin Service crash.
    //
    // Usage: HidSharp.CollisionTest <copy>[+<copy>...] [hotplugSeconds]
    //   e.g. HidSharp.CollisionTest stock+patched
    //
    // Exit codes: 0 = every copy initialized, 2 = process survived but a copy failed to
    // initialize (exception caught), 3 = hot-plug was requested and a copy saw no events.
    // Any other code (e.g. -532462766) means the process crashed.
    static class Program
    {
        sealed class Copy
        {
            public string Name;
            public Type DeviceListType;
            public object Local;
            public EventCounter Events;
        }

        sealed class EventCounter
        {
            public int Count;
            public void OnChanged(object sender, EventArgs e) { Interlocked.Increment(ref Count); }
        }

        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: HidSharp.CollisionTest <stock|patched>[+<stock|patched>...] [hotplugSeconds]");
                return 64;
            }

            if (!OperatingSystem.IsWindows())
                Console.WriteLine("NOTE: not running on Windows; the window-class collision only happens on Windows.");

            var names = args[0].Split('+');
            int hotplugSeconds = args.Length > 1 ? int.Parse(args[1]) : 0;

            var copies = new System.Collections.Generic.List<Copy>();
            bool anyFailed = false;

            for (int i = 0; i < names.Length; i++)
            {
                string label = names[i] + "#" + (i + 1);
                try
                {
                    var copy = Load(names[i], label);
                    int count = CountHidDevices(copy);
                    Console.WriteLine("[{0}] OK, {1} HID device(s)", label, count);
                    copies.Add(copy);
                }
                catch (Exception ex)
                {
                    anyFailed = true;
                    var inner = Unwrap(ex);
                    Console.WriteLine("[{0}] CAUGHT {1}: {2}", label, inner.GetType().Name, inner.Message);
                    if (inner.InnerException != null)
                        Console.WriteLine("[{0}]   caused by {1}: {2}", label, inner.InnerException.GetType().Name, inner.InnerException.Message);
                }
            }

            int exitCode = anyFailed ? 2 : 0;

            if (hotplugSeconds > 0 && copies.Count > 0)
            {
                foreach (var copy in copies) { Subscribe(copy); }
                Console.WriteLine("Plug or unplug a USB HID device within {0}s...", hotplugSeconds);
                Thread.Sleep(hotplugSeconds * 1000);
                foreach (var copy in copies)
                {
                    Console.WriteLine("[{0}] {1} device-change event(s)", copy.Name, copy.Events.Count);
                    if (copy.Events.Count == 0) { exitCode = exitCode == 0 ? 3 : exitCode; }
                }
            }

            Console.WriteLine("Process survived. Exit code {0}.", exitCode);
            return exitCode;
        }

        static Copy Load(string kind, string label)
        {
            if (kind != "stock" && kind != "patched")
                throw new ArgumentException("Unknown copy '" + kind + "' (use stock or patched).");

            string path = Path.Combine(AppContext.BaseDirectory, kind, "HidSharp.dll");
            var context = new AssemblyLoadContext(label, isCollectible: false);
            var assembly = context.LoadFromAssemblyPath(path);
            var type = assembly.GetType("HidSharp.DeviceList", throwOnError: true);
            var local = type.GetProperty("Local").GetValue(null);
            return new Copy { Name = label, DeviceListType = type, Local = local };
        }

        static int CountHidDevices(Copy copy)
        {
            // GetHidDevices has only optional parameters; pass "missing" for each of them.
            var method = copy.DeviceListType.GetMethods()
                .First(m => m.Name == "GetHidDevices" && m.GetParameters().All(p => p.IsOptional));
            var args = method.GetParameters().Select(p => Type.Missing).ToArray();
            int count = 0;
            foreach (var device in (IEnumerable)method.Invoke(copy.Local, args)) { count++; }
            return count;
        }

        static void Subscribe(Copy copy)
        {
            copy.Events = new EventCounter();
            var changed = copy.DeviceListType.GetEvent("Changed");
            var handler = Delegate.CreateDelegate(changed.EventHandlerType, copy.Events, typeof(EventCounter).GetMethod("OnChanged"));
            changed.AddEventHandler(copy.Local, handler);
        }

        static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) { ex = ex.InnerException; }
            return ex;
        }
    }
}
