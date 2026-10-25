using EchaloteVpn.Core.Tor;

namespace EchaloteVpn.Core.Tests;

public class TorReplyReaderTests
{
    [Fact]
    public async Task MultiLineReply_IsReadUntilFinalLine()
    {
        var reader = new StringReader(
            "250-status/bootstrap-phase=NOTICE BOOTSTRAP PROGRESS=45 TAG=loading_descriptors SUMMARY=\"Loading relay descriptors\"\r\n" +
            "250 OK\r\n");

        var reply = await TorReplyReader.ReadAsync(reader);

        Assert.True(reply.IsSuccess);
        Assert.Equal(2, reply.Lines.Count);
        Assert.Equal("OK", reply.Lines[1]);
    }

    [Fact]
    public async Task ErrorReply_KeepsStatusCode()
    {
        var reply = await TorReplyReader.ReadAsync(new StringReader("515 Authentication failed\r\n"));

        Assert.False(reply.IsSuccess);
        Assert.Equal(515, reply.StatusCode);
    }

    [Fact]
    public async Task DataBlock_IsReadUntilDot()
    {
        var reader = new StringReader("250+config-text=\r\nSocksPort 9050\r\n..hidden\r\n.\r\n250 OK\r\n");

        var reply = await TorReplyReader.ReadAsync(reader);

        Assert.Equal(new[] { "config-text=", "SocksPort 9050", ".hidden", "OK" }, reply.Lines);
    }

    [Fact]
    public async Task ClosedConnection_Throws()
    {
        await Assert.ThrowsAsync<TorControlException>(() => TorReplyReader.ReadAsync(new StringReader("250-partial\r\n")));
    }

    [Fact]
    public void BootstrapPhase_IsParsed()
    {
        var status = BootstrapStatus.Parse("NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY=\"Done\"");

        Assert.Equal(100, status.Progress);
        Assert.Equal("done", status.Tag);
        Assert.Equal("Done", status.Summary);
        Assert.True(status.IsDone);
    }
}
