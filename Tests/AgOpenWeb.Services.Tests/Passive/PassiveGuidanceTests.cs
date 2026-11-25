// GNU GPL v3. Exercises the actual AgOpenWeb PP integration, not a duplicate controller.
using System.Globalization;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Models.GPS;
using AgOpenWeb.Models.Pipeline;
using AgOpenWeb.Models.Track;
using AgOpenWeb.Services.Passive;
using AgOpenWeb.Services.Pipeline;
using AgOpenWeb.Services.Profile;
using AgOpenWeb.Services.Track;
namespace AgOpenWeb.Services.Tests.Passive;
[TestFixture]
public class PassiveGuidanceTests
{
    private static readonly DateTime Epoch=new(2026,1,1,0,0,0,DateTimeKind.Utc);
    private static TrackGuidanceInput Input(double seconds=0,bool same=true,bool requested=true,bool enabled=true,double error=0.6,double pivotError=0,bool reverse=false)
    {
        double body=same?0:180;var now=Epoch.AddSeconds(seconds);
        return new() {
            Track=AgOpenWeb.Models.Track.Track.FromABLine("line",new Vec3(0,-100,0),new Vec3(0,300,0)),
            PivotPosition=new Vec3(pivotError,10,body*Math.PI/180),SteerPosition=new Vec3(pivotError,12,body*Math.PI/180),
            Wheelbase=3,MaxSteerAngle=35,GoalPointDistance=5,FixHeading=body*Math.PI/180,AvgSpeed=7.2,
            IsAutoSteerOn=true,IsHeadingSameWay=same,IsReverse=reverse,
            Passive=new() {Settings=new() {Enabled=enabled},Now=now,Requested=requested,HasLocalPlane=true,ToolEast=error,ToolNorth=2,Width=6,Context="profile",
                Fix=new(now,49,-98,4,2,body,double.NaN,double.NaN,now,0)}
        };
    }
    [TestCase(true)] [TestCase(false)]
    public void FeedbackMovesTractorTargetTowardOppositeSideOfToolError(bool same)
    {
        var svc=new TrackGuidanceService();TrackGuidanceOutput result=null!;
        for(int i=0;i<=60;i++)result=svc.CalculateGuidance(Input(i*0.1,same));
        Assert.Multiple(()=>{
            Assert.That(result.Passive.Engaged,Is.True);
            Assert.That(result.Passive.Offset,Is.LessThan(0));
            Assert.That(result.GoalPoint.Easting,Is.LessThan(0));
            Assert.That(double.IsFinite(result.SteerAngle),Is.True);
        });
    }
    [Test]
    public void OffModeDoesNotAlterExistingSteering()
    {
        var a=Input(enabled:false);a.PivotPosition=new Vec3(0.3,10,0);
        var b=Input(enabled:false);b.PivotPosition=a.PivotPosition;b.Passive=null;
        var aa=new TrackGuidanceService().CalculateGuidance(a);var bb=new TrackGuidanceService().CalculateGuidance(b);
        Assert.That(aa.SteerAngle,Is.EqualTo(bb.SteerAngle));Assert.That(aa.GoalPoint,Is.EqualTo(bb.GoalPoint));
    }
    [Test]
    public void ToolButtonAndTractorButtonBothGateGuidance()
    {
        var svc=new TrackGuidanceService();for(int i=0;i<50;i++)svc.CalculateGuidance(Input(i*0.1));
        Assert.That(svc.CalculateGuidance(Input(5,requested:false)).Passive.Offset,Is.Zero);
        var input=Input(5.1);input.IsAutoSteerOn=false;
        Assert.That(svc.CalculateGuidance(input).Passive.Engaged,Is.False);
    }
    [Test]
    public void StaleAndInvalidFixClearAppliedOffsetImmediately()
    {
        var svc=new TrackGuidanceService();for(int i=0;i<50;i++)svc.CalculateGuidance(Input(i*0.1));
        var sample=Input(7);var r=sample.Passive!;
        sample.Passive=new() {Settings=r.Settings,Now=Epoch.AddSeconds(7),Requested=true,HasLocalPlane=true,Width=6,Context="profile",Fix=r.Fix! with {ReceivedUtc=Epoch}};
        Assert.That(svc.CalculateGuidance(sample).Passive.Offset,Is.Zero);
        sample=Input(8);r=sample.Passive!;
        sample.Passive=new() {Settings=r.Settings,Now=r.Now,Requested=true,HasLocalPlane=true,Width=6,Context="profile",Fix=r.Fix! with {FixQuality=0}};
        Assert.That(svc.CalculateGuidance(sample).Passive.Engaged,Is.False);
    }
    [Test]
    public void AcquisitionDoesNotApplyToolOffsetBeforeTractorIsOnLine()
    {
        var result=new TrackGuidanceService().CalculateGuidance(Input(pivotError:0.2));
        Assert.That(result.Passive.Engaged,Is.False);Assert.That(result.Passive.Reason,Does.Contain("Acquire"));
    }
    [Test]
    public void UturnBypassesPassiveAndDoesNotModifyTarget()
    {
        var a=Input();a.IsYouTurnTriggered=true;var b=Input();b.Passive=null;b.IsYouTurnTriggered=true;
        var svc=new TrackGuidanceService();var output=svc.CalculateGuidance(a);var reference=new TrackGuidanceService().CalculateGuidance(b);
        Assert.That(output.Passive.Engaged,Is.False);Assert.That(output.GoalPoint,Is.EqualTo(reference.GoalPoint));
    }
    [Test]
    public void PositionOnlyReceiverDoesNotInventDualHeading()
    {
        var svc=new TrackGuidanceService();TrackGuidanceOutput output=null!;
        for(int i=0;i<30;i++)
        {
            var a=Input(i*0.1);var r=a.Passive!;
            a.Passive=new(){Settings=r.Settings,Now=r.Now,Requested=true,HasLocalPlane=true,ToolEast=.4,ToolNorth=i*.2,Width=6,Context="profile",Fix=r.Fix! with {DualHeading=double.NaN,Speed=double.NaN}};
            output=svc.CalculateGuidance(a);
        }
        Assert.That(output.Passive.HeadingSource,Is.EqualTo("Calculated course"));
    }
    [Test]
    public void OffsetSlewRemainsBounded()
    {
        var svc=new TrackGuidanceService();double previous=0;DateTime previousTime=Epoch;
        for(int i=0;i<300;i++)
        {
            var input=Input(i*.1,error:i<150?.6:-.6);
            var result=svc.CalculateGuidance(input);
            double maxStep=(input.Passive!.Now-previousTime).TotalSeconds*.2;previousTime=input.Passive.Now;
            Assert.That(Math.Abs(result.Passive.Offset-previous),Is.LessThanOrEqualTo(maxStep+1e-9));
            Assert.That(Math.Abs(result.Passive.Offset),Is.LessThanOrEqualTo(1));previous=result.Passive.Offset;
        }
    }
    [Test]
    public void ManualSettingsRoundTripWithToolAndOldProfilesDefaultDisabled()
    {
        var dir=Path.Combine(Path.GetTempPath(),"passive-"+Guid.NewGuid().ToString("N",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
        try {
            var src=new ConfigurationStore();src.Tool.Passive=new(){Enabled=true,Interval=4.7,CurveResponse=133,UseGeometry=true,HeadingOffset=5};
            ToolProfileJsonService.Save(dir,"tool",src);var dst=new ConfigurationStore();Assert.That(ToolProfileJsonService.Load(dir,"tool",dst),Is.True);
            Assert.Multiple(()=>{Assert.That(dst.Tool.Passive.Enabled,Is.True);Assert.That(dst.Tool.Passive.Interval,Is.EqualTo(4.7));Assert.That(dst.Tool.Passive.CurveResponse,Is.EqualTo(133));});
            var path=Path.Combine(dir,"tool.json");var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;json["tool"]!.AsObject().Remove("passive");File.WriteAllText(path,json.ToJsonString());
            Assert.That(ToolProfileJsonService.Load(dir,"tool",dst),Is.True);Assert.That(dst.Tool.Passive.Enabled,Is.False);
        } finally {Directory.Delete(dir,true);}
    }
    [Test]
    public void RequestUsesLastWinsIntentAndIsConsumedOnlyOnce()
    {
        var intents=new PipelineIntents();intents.RequestPassiveGuidance(true);intents.RequestPassiveGuidance(false);
        Assert.That(intents.Drain().PassiveRequested,Is.False);Assert.That(intents.Drain().PassiveRequested,Is.Null);
    }
    [Test]
    public void SingleGnssImuFusesButUnanchoredYawIsNotAbsoluteHeading()
    {
        var svc=new TrackGuidanceService();TrackGuidanceOutput output=null!;
        for(int i=0;i<20;i++)
        {
            var a=Input(i*.1);var r=a.Passive!;
            a.Passive=new(){Settings=r.Settings,Now=r.Now,Requested=true,HasLocalPlane=true,ToolEast=.2,ToolNorth=i*.2,Width=6,Context="profile",Fix=r.Fix! with {DualHeading=double.NaN,ImuHeading=30,Course=0}};
            output=svc.CalculateGuidance(a);
        }
        Assert.That(output.Passive.HeadingSource,Is.EqualTo("GNSS + IMU"));
        var resolver=new CToolHeadingResolver();
        Assert.That(resolver.Resolve(Epoch,0,0,true,Epoch,0,false,true,double.NaN,DateTime.MinValue,double.NaN,DateTime.MinValue,30,Epoch,0,out _),Is.False);
    }
    [Test]
    public void GeometryCompensationIsOutwardAndSymmetric()
    {
        var g=new CPassiveGeometry {Trailing=true,Hitch=-1.5,Drawbar=8,WorkingBar=2,Width=6};
        Assert.That(g.Offset(.04),Is.LessThan(0));Assert.That(g.Offset(-.04),Is.EqualTo(-g.Offset(.04)).Within(1e-12));
        double vehicleRadius=25-g.Offset(.04);
        Assert.That(Math.Sqrt(vehicleRadius*vehicleRadius+1.5*1.5-8*8+2*2),Is.EqualTo(25).Within(1e-9));
    }
    [Test]
    public void InvalidProfileSettingsDisableModeAndStaySerializable()
    {
        var p=PassiveSettings.Sanitize(new(){Enabled=true,Interval=double.NaN,StraightLimit=100,CurveResponse=133});
        Assert.Multiple(()=>{Assert.That(p.Enabled,Is.False);Assert.That(p.Interval,Is.EqualTo(4));Assert.That(p.StraightLimit,Is.EqualTo(3));Assert.That(p.CurveResponse,Is.EqualTo(133));});
        Assert.DoesNotThrow(()=>System.Text.Json.JsonSerializer.Serialize(p));
    }
    [Test]
    public void InvalidPathClearsHeldCorrectionBeforeNextAcquisition()
    {
        var svc=new TrackGuidanceService();for(int i=0;i<50;i++)svc.CalculateGuidance(Input(i*.1));
        var bad=Input(5);bad.Track.Points.Clear();svc.CalculateGuidance(bad);
        var after=svc.CalculateGuidance(Input(5.1,pivotError:.2));
        Assert.That(after.Passive.Engaged,Is.False);Assert.That(after.Passive.Offset,Is.Zero);
    }
    [Test]
    public void DistantImplementFixCannotDriveTractorOffset()
    {
        var output=new TrackGuidanceService().CalculateGuidance(Input(error:200));
        Assert.That(output.Passive.Engaged,Is.False);Assert.That(output.Passive.Reason,Does.Contain("too far"));
    }
    private static string Sentence(string body)
    {int sum=0;foreach(var c in body)sum^=c;return "$"+body+"*"+sum.ToString("X2",CultureInfo.InvariantCulture);}
    [TestCase("PAOGI", "90", "1", 90.0)] [TestCase("PANDA", "900", "2", 90.0)]
    public void ToolDualSentencesDecodeSeparately(string type,string heading,string source,double expected)
    {
        string text=Sentence(type+",120000,4900.000,N,09800.000,W,4,14,0.8,300,0.5,4,"+heading+",0,0,0,"+source+",1,100,88");
        Assert.That(ImplementGpsReceiver.TryDecode(text,Epoch,out var fix),Is.True);
        Assert.Multiple(()=>{Assert.That(fix!.Latitude,Is.EqualTo(49));Assert.That(fix!.Longitude,Is.EqualTo(-98));Assert.That(type=="PAOGI"?fix!.DualHeading:fix!.ImuHeading,Is.EqualTo(expected));Assert.That(fix!.HeadingUtc,Is.EqualTo(Epoch.AddMilliseconds(-100)));});
    }
    [Test]
    public void BadChecksumAndInvalidExtensionDoNotInventHeading()
    {
        string text=Sentence("PAOGI,120000,4900.000,N,09800.000,W,4,14,0.8,300,0.5,4,90,0,0,0,1,0,100,88");
        Assert.That(ImplementGpsReceiver.TryDecode(text,Epoch,out var fix),Is.True);Assert.That(double.IsNaN(fix!.DualHeading),Is.True);
        Assert.That(ImplementGpsReceiver.TryDecode(text[..^2]+"FF",Epoch,out _),Is.False);
    }
    [Test]
    public void ToolPortReceiverDoesNotPublishAnythingToTractorGps()
    {
        using var receiver=new ImplementGpsReceiver();receiver.ProcessDatagram(System.Text.Encoding.ASCII.GetBytes(Sentence("PANDA,120000,4900.000,N,09800.000,W,4,14,0.8,300,0.5,4,65535,0,0,0")),Epoch);
        Assert.That(receiver.Latest,Is.Not.Null);Assert.That(double.IsNaN(receiver.Latest!.ImuHeading),Is.True);
        receiver.Dispose();Assert.That(receiver.Latest,Is.Null);
    }
    [TestCase(0)] [TestCase(45)] [TestCase(180)] [TestCase(270)]
    public void SimulatedAntennaOffsetsRecoverTheIndependentWorkingBar(double degrees)
    {
        var input=Input(0);
        var settings=input.Passive!.Settings;
        settings.AntennaOffset=1.2;settings.AntennaToWorkingPoint=3.7;
        settings.HeadingOffset=13;settings.RollZero=2;settings.InvertRoll=true;
        double heading=degrees*Math.PI/180;
        var antenna=ToolGpsSimulator.AntennaPosition(0.6,2,heading,settings);
        var fix=new ImplementGpsFix(Epoch,0,0,4,2,degrees-settings.HeadingOffset,double.NaN,double.NaN,Epoch,-settings.RollZero);
        input.Passive=new() {Settings=settings,Now=Epoch,Requested=true,HasLocalPlane=true,ToolEast=antenna.East,ToolNorth=antenna.North,Fix=fix,Width=6,Context="sim"};
        var result=new TrackGuidanceService().CalculateGuidance(input);
        Assert.Multiple(()=>{
            Assert.That(result.Passive.ToolEast,Is.EqualTo(0.6).Within(1e-9));
            Assert.That(result.Passive.ToolNorth,Is.EqualTo(2).Within(1e-9));
        });
    }
    [Test]
    public void ToolGpsIntentIsIndependentFromPassiveRequestAndLastWins()
    {
        var intents=new PipelineIntents();
        intents.RequestPassiveGuidance(true);
        intents.RequestToolGpsSimulation(true);intents.RequestToolGpsSimulation(false);
        var batch=intents.Drain();
        Assert.That(batch.PassiveRequested,Is.True);
        Assert.That(batch.ToolGpsSimulation,Is.False);
        Assert.That(intents.Drain().ToolGpsSimulation,Is.Null);
    }

}
