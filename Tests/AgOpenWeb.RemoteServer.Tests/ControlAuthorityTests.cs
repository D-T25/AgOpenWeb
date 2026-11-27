// GNU GPL v3. Deterministic deadlines; no sleeps or physical steering hardware.
using NUnit.Framework;
using AgOpenWeb.RemoteServer;
namespace AgOpenWeb.RemoteServer.Tests;
public class ControlAuthorityTests
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    [Test]
    public void UiPulsesKeepTheOriginalDeadlineFresh()
    {
        var clock=new Clock();var authority=new ControlAuthority(clock);var client=Guid.NewGuid();authority.Acquire(client,"operator");
        for(int i=0;i<20;i++){clock.Milliseconds+=250;authority.Refresh(client);Assert.That(authority.HoldsFresh(client),Is.True);}
        clock.Milliseconds+=1500;Assert.That(authority.HoldsFresh(client),Is.True);
        clock.Milliseconds++;Assert.That(authority.HoldsFresh(client),Is.False);
    }
    [Test]
    public void LatePulseCannotReviveExpiredAuthorityBeforeSweep()
    {
        var clock=new Clock();var authority=new ControlAuthority(clock);var client=Guid.NewGuid();int revoked=0;
        authority.Revoked+=_=>revoked++;authority.Acquire(client,"operator");clock.Milliseconds=1501;
        authority.Refresh(client);authority.SweepStale();
        Assert.Multiple(()=>{Assert.That(authority.HoldsFresh(client),Is.False);Assert.That(authority.Snapshot().Held,Is.False);Assert.That(revoked,Is.EqualTo(1));});
    }
    [Test]
    public void ObserverCannotRenewOrTakeLiveAuthority()
    {
        var clock=new Clock();var authority=new ControlAuthority(clock);var owner=Guid.NewGuid();var observer=Guid.NewGuid();authority.Acquire(owner,"owner");
        clock.Milliseconds=1400;authority.Refresh(observer);Assert.That(authority.Acquire(observer,"observer"),Is.False);
        clock.Milliseconds=1501;Assert.That(authority.SweepStale(),Is.True);Assert.That(authority.HoldsFresh(owner),Is.False);
    }
    [Test]
    public void DisconnectedAndFrozenUiBothRevoke()
    {
        var clock=new Clock();var authority=new ControlAuthority(clock);var client=Guid.NewGuid();int revoked=0;authority.Revoked+=_=>revoked++;
        authority.Acquire(client,"operator");authority.Drop(client);Assert.That(revoked,Is.EqualTo(1));
        authority.Acquire(client,"operator");clock.Milliseconds=1501;Assert.That(authority.SweepStale(),Is.True);
        Assert.That(revoked,Is.EqualTo(2));Assert.That(authority.SweepStale(),Is.False);
    }
}
