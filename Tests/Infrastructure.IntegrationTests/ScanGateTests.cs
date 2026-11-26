using IdCardReader.Infrastructure;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>The gate that keeps a CPU-only server from queueing scans without limit.</summary>
public sealed class ScanGateTests
{
    [Fact]
    public void TryEnter_AdmitsUpToTheCapacityThenRefuses()
    {
        var gate = new ScanGate(2);

        Assert.True(gate.TryEnter());
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.Equal(2, gate.Admitted);
    }

    [Fact]
    public void Exit_FreesASlot()
    {
        var gate = new ScanGate(1);
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());

        gate.Exit();

        Assert.Equal(0, gate.Admitted);
        Assert.True(gate.TryEnter());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Capacity_IsAtLeastOne(int capacity)
    {
        var gate = new ScanGate(capacity);

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public async Task TryEnter_NeverAdmitsMoreThanTheCapacityUnderContention()
    {
        const int capacity = 3;
        var gate = new ScanGate(capacity);
        var inside = 0;
        var maxInside = 0;
        var gateObject = new object();

        var workers = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                if (!gate.TryEnter())
                {
                    continue;
                }

                var now = Interlocked.Increment(ref inside);
                lock (gateObject)
                {
                    maxInside = Math.Max(maxInside, now);
                }

                Interlocked.Decrement(ref inside);
                gate.Exit();
            }
        }));

        await Task.WhenAll(workers);

        Assert.True(maxInside <= capacity, $"{maxInside} scans were inside a gate of {capacity}.");
        Assert.Equal(0, gate.Admitted);
    }
}
