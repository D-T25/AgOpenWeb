// GNU GPL v3. Profile-boundary validation for manual settings.
using AgOpenWeb.Models.Configuration;
namespace AgOpenWeb.Services.Passive;
public static class PassiveSettings
{
    public static PassiveGuidanceConfig Sanitize(PassiveGuidanceConfig? source)
    {
        if(source==null)return new();
        var p=new PassiveGuidanceConfig {Enabled=source.Enabled,EarlyRetry=source.EarlyRetry,UseGeometry=source.UseGeometry,InvertRoll=source.InvertRoll};
        double Value(double value,double min,double max,double fallback)
        {if(!double.IsFinite(value)){p.Enabled=false;return fallback;}return Math.Clamp(value,min,max);}
        p.Tracking=Value(source.Tracking,50,200,100);p.Heading=Value(source.Heading,50,200,100);p.Acquire=Value(source.Acquire,50,200,100);
        p.CurveResponse=Value(source.CurveResponse,0,300,100);p.Interval=Value(source.Interval,0,25,4);p.Strength=Value(source.Strength,50,200,100);
        p.StraightLimit=Value(source.StraightLimit,.1,3,1);p.CurveLimit=Value(source.CurveLimit,.1,3,1);p.LookAhead=Value(source.LookAhead,0,2,0);
        p.AntennaHeight=Value(source.AntennaHeight,0,10,0);p.AntennaOffset=Value(source.AntennaOffset,-10,10,0);p.AntennaToWorkingPoint=Value(source.AntennaToWorkingPoint,-30,30,0);
        p.HeadingOffset=Value(source.HeadingOffset,-180,180,0);p.RollZero=Value(source.RollZero,-45,45,0);
        return p;
    }
}
