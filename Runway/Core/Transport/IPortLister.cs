namespace Runway.Transport;

public interface IPortLister
{
    List<string> GetAvailablePorts();
}
