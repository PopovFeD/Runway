//  dotnet run --project com_reading.csproj -- --port COM6 --baud 115200
using System;
using System.IO;
using System.IO.Ports;

string portName = "COM6";
int baudRate = 115200;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port":
            portName = args[++i];
            break;
        case "--baud":
            baudRate = int.Parse(args[++i]);
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Environment.Exit(1);
            break;
    }
}

bool running = true;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    running = false;
};

try
{
    using SerialPort port = new(portName, baudRate) { ReadTimeout = 1000, WriteTimeout = 1000 };

    port.Open();
    Console.WriteLine($"Listening on {portName} at {baudRate} baud. Press Ctrl+C to stop.");

    while (running)
    {
        try
        {
            int value = port.ReadByte();
            Console.WriteLine($"Received byte: {value} ({(char)value})");
        }
        catch (TimeoutException)
        {
            continue;
        }
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Invalid port settings: {ex.Message}");
    Environment.Exit(1);
}
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine($"Access denied: {ex.Message}");
    Environment.Exit(1);
}
catch (IOException ex)
{
    Console.Error.WriteLine($"I/O error: {ex.Message}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Environment.Exit(1);
}
