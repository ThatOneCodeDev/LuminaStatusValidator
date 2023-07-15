using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

namespace ServiceStatusChecker
{
    class Program
    {
        static List<Server> servers;
        static TimeSpan refreshTime = TimeSpan.FromMinutes(5);
        static System.Timers.Timer refreshTimer;
        static DateTime lastRefreshTime;
        static System.Timers.Timer titleUpdateTimer;

        static async Task Main(string[] args)
        {
            Console.Title = "Lumina - Status Validator";
            LoadServers();

            // Start a separate task to handle user input
            var userInputTask = Task.Run(HandleUserInput);

            // Perform initial server status check
            await DisplayServerStatuses();

            // Start the refresh timer
            refreshTimer = new System.Timers.Timer(refreshTime.TotalMilliseconds);
            refreshTimer.Elapsed += RefreshTimerElapsed;
            lastRefreshTime = DateTime.Now;
            refreshTimer.Start();

            // Start the title update timer
            titleUpdateTimer = new System.Timers.Timer(1000);
            titleUpdateTimer.Elapsed += TitleUpdateTimerElapsed;
            titleUpdateTimer.Start();

            while (true)
            {
                await Task.Delay(1000);
            }
        }

        static async Task DisplayServerStatuses()
        {
            Console.Clear();

            foreach (var server in servers)
            {
                try
                {
                    await CheckServerStatus(server);
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking server status for '{server.Name}': {ex.Message}");
                    Console.WriteLine();
                }
            }
        }

        static async Task CheckServerStatus(Server server)
        {
            if (server == null)
            {
                Console.WriteLine("Null server entry found.");
                return;
            }

            if (server.MaintenanceMode)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"Server: {server.Name} is in maintenance mode.");
            }
            else
            {
                try
                {
                    PingReply reply = await new Ping().SendPingAsync(server.IPAddress);

                    if (reply.Status == IPStatus.Success)
                    {
                        Console.ForegroundColor = GetLatencyColor(reply.RoundtripTime);
                        Console.WriteLine($"Server: {server.Name} is operational.");
                        Console.WriteLine($"Latency: {reply.RoundtripTime} ms");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Server: {server.Name} is down.");
                    }
                }
                catch (PingException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error pinging server '{server.Name}': {ex.Message}");
                }
            }

            Console.ResetColor();
        }

        static ConsoleColor GetLatencyColor(long latency)
        {
            if (latency >= 700)
                return ConsoleColor.Red;
            if (latency >= 300)
                return ConsoleColor.Yellow;
            return ConsoleColor.Green;
        }

        static void ToggleMaintenanceMode(Server server)
        {
            if (server == null)
            {
                Console.WriteLine("Null server entry found.");
                return;
            }

            server.MaintenanceMode = !server.MaintenanceMode;
            Console.WriteLine($"Server: {server.Name} is now {(server.MaintenanceMode ? "in maintenance mode" : "online")}");

            SaveServers();
        }

        static void ConfigureRefreshTime(int minutes)
        {
            refreshTime = TimeSpan.FromMinutes(minutes);
            refreshTimer.Interval = refreshTime.TotalMilliseconds;
            Console.WriteLine($"Refresh time has been set to {minutes} minutes.");

            // Update the title with the new refresh time
            UpdateTitleBar();
        }

        static void DisplayHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("- help: Display the available commands");
            Console.WriteLine("- info <ServerName>: Get detailed information about a server");
            Console.WriteLine("- maintenance <ServerName>: Toggle maintenance mode for a server");
            Console.WriteLine("- config <Minutes>: Configure the refresh time in minutes");
            Console.WriteLine("- clear: Clear the screen and display the latest information");
            Console.WriteLine("- exit: Exit the program");
            Console.WriteLine("- add: Add a new server to the list");
            Console.WriteLine("- edit: Edit an existing server in the list");
            Console.WriteLine("- remove: Remove a server from the list");
            Console.WriteLine("- query <ServerName> <PortNumber>: Query a server's port status");
        }

        static void LoadServers()
        {
            if (File.Exists("config.json"))
            {
                try
                {
                    string json = File.ReadAllText("config.json");
                    servers = JsonSerializer.Deserialize<List<Server>>(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading server list: {ex.Message}");
                    servers = new List<Server>();
                }
            }
            else
            {
                servers = new List<Server>();
            }
        }

        static void SaveServers()
        {
            try
            {
                string json = JsonSerializer.Serialize(servers);
                File.WriteAllText("config.json", json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving server list: {ex.Message}");
            }
        }

        static Server GetServerByName(string name)
        {
            return servers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        static async Task AddServer()
        {
            Console.WriteLine("Adding a new server...");

            Console.Write("Enter the server name: ");
            string input = Console.ReadLine();

            if (input.StartsWith('"') && input.EndsWith('"'))
            {
                input = input.Trim('"');
            }

            if (GetServerByName(input) != null)
            {
                Console.WriteLine("A server with the same name already exists.");
                return;
            }

            Console.Write("Enter the server IP address: ");
            string ipAddress = Console.ReadLine();

            var newServer = new Server(input, ipAddress);
            servers.Add(newServer);
            SaveServers();

            Console.WriteLine("Server added successfully.");

            await DisplayServerStatuses();
        }

        static async Task EditServer()
        {
            Console.WriteLine("Editing an existing server...");

            Console.Write("Enter the server name: ");
            string input = Console.ReadLine();

            if (input.StartsWith('"') && input.EndsWith('"'))
            {
                input = input.Trim('"');
            }

            var server = GetServerByName(input);
            if (server == null)
            {
                Console.WriteLine("Server not found.");
                return;
            }

            Console.Write("Enter the new server IP address: ");
            string ipAddress = Console.ReadLine();

            server.IPAddress = ipAddress;

            SaveServers();
            Console.WriteLine("Server edited successfully.");

            await DisplayServerStatuses();
        }

        static async Task RemoveServer()
        {
            Console.WriteLine("Removing a server...");

            Console.Write("Enter the server name: ");
            string input = Console.ReadLine();

            if (input.StartsWith('"') && input.EndsWith('"'))
            {
                input = input.Trim('"');
            }

            var server = GetServerByName(input);
            if (server == null)
            {
                Console.WriteLine("Server not found.");
                return;
            }

            servers.Remove(server);
            SaveServers();

            Console.WriteLine("Server removed successfully.");

            await DisplayServerStatuses();
        }

        static async Task DisplayServerInfo(Server server)
        {
            Console.WriteLine($"Server: {server.Name}");

            IPAddress ipAddress;
            if (IPAddress.TryParse(server.IPAddress, out ipAddress))
            {
                Console.WriteLine($"IP Address: {ipAddress}");
            }
            else
            {
                try
                {
                    IPAddress[] addressList = await Dns.GetHostAddressesAsync(server.IPAddress);
                    ipAddress = addressList.FirstOrDefault()!;
                    if (ipAddress != null)
                    {
                        Console.WriteLine($"Resolved IP Address: {ipAddress}");
                    }
                    else
                    {
                        Console.WriteLine($"Unable to resolve IP address for '{server.Name}'.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error resolving IP address for '{server.Name}': {ex.Message}");
                }
            }

            if (server.MaintenanceMode)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("Status: In Maintenance");
                Console.ResetColor();
            }
            else
            {
                try
                {
                    if (ipAddress != null)
                    {
                        PingReply reply = await new Ping().SendPingAsync(ipAddress);
                        Console.ForegroundColor = GetLatencyColor(reply.RoundtripTime);
                        Console.WriteLine("Status: Online");
                        Console.WriteLine($"Latency: {reply.RoundtripTime} ms");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Status: Offline");
                    }
                }
                catch (PingException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error pinging server '{server.Name}': {ex.Message}");
                    Console.ResetColor();
                }
            }
        }


        static async Task HandleUserInput()
        {
            while (true)
            {
                // Process user input
                Console.WriteLine("\nEnter a command (type 'help' for a list of commands):");
                string command = Console.ReadLine()!;

                if (command == "help")
                {
                    DisplayHelp();
                }
                else if (command.StartsWith("info"))
                {
                    string[] parts = command.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        string serverName = parts[1];
                        if (serverName.StartsWith('"') && serverName.EndsWith('"'))
                        {
                            serverName = serverName.Trim('"');
                        }
                        var server = GetServerByName(serverName);
                        if (server != null)
                        {
                            await DisplayServerInfo(server);
                        }
                        else
                        {
                            Console.WriteLine($"Server '{serverName}' does not exist.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid command format. Usage: info <ServerName>");
                    }
                }
                else if (command.StartsWith("maintenance"))
                {
                    string[] parts = command.Split(' ', 2);
                    if (parts.Length == 2 || parts.Length == 1)
                    {
                        string serverName = parts.Length == 2 ? parts[1] : parts[0];
                        if (serverName.StartsWith('"') && serverName.EndsWith('"'))
                        {
                            serverName = serverName.Trim('"');
                        }
                        var server = GetServerByName(serverName);
                        if (server != null)
                        {
                            ToggleMaintenanceMode(server);
                        }
                        else
                        {
                            Console.WriteLine($"Server '{serverName}' does not exist.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid command format. Usage: maintenance <ServerName>");
                    }
                }
                else if (command.StartsWith("config"))
                {
                    string[] parts = command.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[1], out int minutes))
                        {
                            ConfigureRefreshTime(minutes);
                        }
                        else
                        {
                            Console.WriteLine("Invalid refresh time. Please enter a valid integer.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid command format. Usage: config <Minutes>");
                    }
                }
                else if (command == "clear")
                {
                    await DisplayServerStatuses();
                }
                else if (command == "exit")
                {
                    SaveServers();
                    Environment.Exit(0);
                }
                else if (command == "add")
                {
                    await AddServer();
                    SaveServers();
                }
                else if (command == "edit")
                {
                    await EditServer();
                    SaveServers();
                }
                else if (command == "remove")
                {
                    await RemoveServer();
                    SaveServers();
                }
                else if (command.StartsWith("query"))
                {
                    string[] parts = command.Split(' ', 3);
                    if (parts.Length == 3)
                    {
                        string serverName = parts[1];
                        string portNumberStr = parts[2];
                        if (serverName.StartsWith('"') && serverName.EndsWith('"'))
                        {
                            serverName = serverName.Trim('"');
                        }
                        if (int.TryParse(portNumberStr, out int portNumber))
                        {
                            var server = GetServerByName(serverName);
                            if (server != null)
                            {
                                QueryServerPort(server, portNumber);
                            }
                            else
                            {
                                Console.WriteLine($"Server '{serverName}' does not exist.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Invalid port number. Please enter a valid integer.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid command format. Usage: query <ServerName> <PortNumber>");
                    }
                }
            }
        }

        static void RefreshTimerElapsed(object sender, ElapsedEventArgs e)
        {
            lastRefreshTime = DateTime.Now;
            Task.Run(DisplayServerStatuses);
            UpdateTitleBar();
        }

        static void TitleUpdateTimerElapsed(object sender, ElapsedEventArgs e)
        {
            UpdateTitleBar();
        }

        static void UpdateTitleBar()
        {
            string refreshTimeText = refreshTime.TotalMinutes == 1 ? "1 minute" : $"{refreshTime.TotalMinutes} minutes";
            TimeSpan timeElapsed = DateTime.Now - lastRefreshTime;
            double timeLeft = Math.Max(refreshTime.TotalMilliseconds - timeElapsed.TotalMilliseconds, 0);
            string timeLeftText = $"{Math.Ceiling(timeLeft / 1000):F0} seconds";

            // Show refreshing when timeLeft reaches 0
            string nextRefreshText = timeLeft <= 0 ? "Refreshing..." : $"Next Refresh In: {timeLeftText}";

            // Show UTC time of last reset
            string lastRefreshUtcText = $"Last Refresh (UTC): {lastRefreshTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")}";

            Console.Title = $"Lumina - Status Validator | {nextRefreshText} | Refresh Time: {refreshTimeText} | {lastRefreshUtcText}";
        }

        static void QueryServerPort(Server server, int portNumber)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect(server.IPAddress, portNumber);
                    Console.WriteLine($"Port {portNumber} on server '{server.Name}' is open.");
                }
            }
            catch
            {
                Console.WriteLine($"Port {portNumber} on server '{server.Name}' is closed.");
            }
        }
    }

    class Server
    {
        public string Name { get; set; }
        public string IPAddress { get; set; }
        public bool MaintenanceMode { get; set; }

        public Server(string name, string ipAddress)
        {
            Name = name;
            IPAddress = ipAddress;
            MaintenanceMode = false;
        }
    }
}
