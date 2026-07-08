using System.IO.Ports;

namespace Runway.Transport;

public class SerialPortLister : IPortLister
{
    public List<string> GetAvailablePorts()
    {
        // SerialPort.GetPortNames() сам работает кроссплатформенно:
        // на Windows вернёт "COM3", "COM6" и т.п.,
        // на Linux — "/dev/ttyUSB0" и т.п.
        string[] names = SerialPort.GetPortNames();
        return new List<string>(names);
    }
}
