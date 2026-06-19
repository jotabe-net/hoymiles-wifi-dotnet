using AwesomeAssertions;

namespace HoymilesProtobufClient.Tests;

[TestClass]
public sealed class GetRealDataNewIntegrationTests
{
    private const string DtuHost = "192.168.1.117";

    [TestMethod]
    public async Task GetRealDataNewAsync_ReturnsPayload()
    {
        var client = new HoymilesProtobufClient(
            DtuHost,
            isEncrypted: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        RealDataNewReqDTO? response = null;
        Func<Task> act = async () => response = await client.GetRealDataNewAsync(cts.Token).ConfigureAwait(false);

        await act.Should().NotThrowAsync();
        response.Should().NotBeNull();
        response!.CalculateSize().Should().BeGreaterThan(0);
    }
}
