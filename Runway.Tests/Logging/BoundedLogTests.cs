using System.Collections.ObjectModel;
using Runway.Logging;
using Xunit;

namespace Runway.Tests.Logging;

public class BoundedLogTests
{
    [Fact]
    public void Add_KeepsAllEntries_WhenUnderCapacity()
    {
        var entries = new ObservableCollection<string>();
        var log = new BoundedLog(entries, capacity: 5);

        log.Add("one");
        log.Add("two");
        log.Add("three");

        Assert.Equal(new[] { "one", "two", "three" }, entries);
    }

    [Fact]
    public void Add_RemovesOldestEntries_WhenOverCapacity()
    {
        var entries = new ObservableCollection<string>();
        var log = new BoundedLog(entries, capacity: 3);

        for (int i = 1; i <= 5; i++)
        {
            log.Add($"line{i}");
        }

        // Держим только последние 3 — самые старые (line1, line2) вытеснены,
        // порядок оставшихся сохраняется.
        Assert.Equal(new[] { "line3", "line4", "line5" }, entries);
    }

    [Fact]
    public void Add_StaysAtCapacity_WhenAddingOneEntryAtATime()
    {
        var entries = new ObservableCollection<string>();
        var log = new BoundedLog(entries, capacity: 1);

        log.Add("first");
        Assert.Equal(new[] { "first" }, entries);

        log.Add("second");
        Assert.Equal(new[] { "second" }, entries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenCapacityIsNotPositive(int capacity)
    {
        var entries = new ObservableCollection<string>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLog(entries, capacity));
    }
}
