using Runway.Transport;

namespace Runway.Tests.Support;

public class FakePortLister : IPortLister
{
    private readonly List<string> _ports;

    public FakePortLister(params string[] ports) => _ports = ports.ToList();

    public List<string> GetAvailablePorts() => _ports;
}
