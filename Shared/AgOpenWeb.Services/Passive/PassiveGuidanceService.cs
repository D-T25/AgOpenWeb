// GNU GPL v3. Twol's manual feedback, heading and geometry running on the cycle worker.
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Pipeline;
using AgOpenWeb.Models.Track;
namespace AgOpenWeb.Services.Passive;
public sealed class PassiveGuidanceService
{
    private readonly CPassiveController controller=new();
    private readonly CToolHeadingResolver heading=new();
    private bool engaged;
    private double applied,curveAverage;
    private DateTime last=DateTime.MinValue;
    private string context="";
    public void Reset(){engaged=false;applied=curveAverage=0;last=DateTime.MinValue;controller.Reset();heading.Reset();}
    public PassiveGuidanceStatus Apply(TrackGuidanceInput input,TrackGuidanceOutput output)
    {
        var r=input.Passive;if(r==null){Reset();return new();}
        var s=r.Settings;var fix=r.Fix;
        if(new[]{s.Tracking,s.Heading,s.Acquire,s.CurveResponse,s.Interval,s.Strength,s.StraightLimit,s.CurveLimit,s.LookAhead,s.AntennaHeight,s.AntennaOffset,s.AntennaToWorkingPoint,s.HeadingOffset,s.RollZero}.Any(v=>!double.IsFinite(v)))
        {Reset();return new(r.Requested,false,Reason:"Check passive settings");}
        var geometry=new CPassiveGeometry {Trailing=r.Trailing,TowBetween=r.TowBetween,Front=r.Front,
            Articulated=r.Articulated,Hitch=r.Hitch,Drawbar=r.Drawbar,TankDrawbar=r.TankDrawbar,WorkingBar=r.WorkingBar,Width=r.Width};
        string key=r.Context+"|"+geometry.Signature;
        if(context!=key){Reset();context=key;}
        string? blocked=!s.Enabled?"Disabled":!r.Requested?"Tool guidance off":!input.IsAutoSteerOn?"Tractor steering off":input.IsYouTurnTriggered?"U-turn":input.UseStanley?"Requires Pure Pursuit":
            !r.HasLocalPlane?"Waiting for field coordinates":fix==null?(r.InputError.Length>0?r.InputError:"Waiting for implement GPS on port 19999"):
            !CToolHeadingResolver.Fresh(r.Now,fix.ReceivedUtc,1.5)||fix.FixQuality<=0?"Implement GPS invalid or stale":
            Math.Abs(input.AvgSpeed)<2?"Below 2 km/h":s.UseGeometry&&geometry.Problem!=null?geometry.Problem:null;
        if(blocked!=null){Reset();return new(r.Requested,false,Reason:blocked);}
        if(Math.Sqrt(Math.Pow(r.ToolEast-input.PivotPosition.Easting,2)+Math.Pow(r.ToolNorth-input.PivotPosition.Northing,2))>100)
        {Reset();return new(r.Requested,false,Reason:"Implement GPS is too far from tractor");}
        // A valid fix is guaranteed above. Heading may be absent; use the antenna position then.
        var f=fix!;bool hasHeading=heading.Resolve(r.Now,r.ToolEast,r.ToolNorth,true,f.ReceivedUtc,f.Speed,
            input.IsReverse,f.FixQuality==4,CToolHeadingResolver.WrapDegrees(f.DualHeading+s.HeadingOffset),f.HeadingUtc,
            f.Course,f.ReceivedUtc,f.ImuHeading,f.HeadingUtc,s.HeadingOffset,out double body);
        double east=r.ToolEast,north=r.ToolNorth;
        if(hasHeading)
        {
            bool validRoll=heading.Source=="Dual GNSS" || (double.IsFinite(f.ImuHeading)&&CToolHeadingResolver.Fresh(r.Now,f.HeadingUtc,0.5));
            double h=body*Math.PI/180,roll=validRoll&&double.IsFinite(f.Roll)?(s.InvertRoll?-f.Roll:f.Roll)-s.RollZero:0;
            double lateral=s.AntennaOffset-Math.Sin(roll*Math.PI/180)*s.AntennaHeight;
            east+=Math.Cos(h)*lateral-Math.Sin(h)*s.AntennaToWorkingPoint;
            north-=Math.Sin(h)*lateral+Math.Cos(h)*s.AntennaToWorkingPoint;
        }
        var points=input.Track!.Points;
        var tool=Project(points,east,north,input.Track.IsClosed);
        var tractor=Project(points,input.PivotPosition.Easting,input.PivotPosition.Northing,input.Track.IsClosed);
        if(tool.Index<0||tractor.Index<0||!double.IsFinite(east)||!double.IsFinite(north))
        {Reset();return new(r.Requested,false,Reason:"Invalid implement/path coordinates");}
        double direction=input.IsHeadingSameWay?1:-1;
        double error=tool.Error*direction;
        // Match Twol: acquire tractor on line before applying implement correction.
        if(!engaged)
        {
            double hd=Math.Abs(CToolHeadingResolver.Difference(input.FixHeading*180/Math.PI,tractor.Heading*180/Math.PI+(input.IsHeadingSameWay?0:180)));
            if(Math.Abs(tractor.Error)>=0.10||hd>=1.5)return new(r.Requested,false,error,HeadingSource:heading.Source,Reason:"Acquire tractor within 10 cm and 1.5 degrees",ToolEast:east,ToolNorth:north,ToolValid:true);
            engaged=true;last=r.Now;
        }
        double local=CPassiveCurve.Curvature(points,tractor.Index,input.Track.IsClosed,true,tractor.Station,2);
        double toolCurve=CPassiveCurve.Curvature(points,tool.Index,input.Track.IsClosed,true,tool.Station,2);
        double preview=CPassiveCurve.Curvature(points,tractor.Index,input.Track.IsClosed,true,
            tractor.Station+(input.IsHeadingSameWay!=input.IsReverse?1:-1)*Math.Clamp(geometry.Length*0.5+Math.Abs(input.AvgSpeed)/3.6,2,15),2);
        double limit=CPassiveCurve.Limit(s.StraightLimit,s.CurveLimit,Math.Max(Math.Abs(toolCurve),Math.Max(Math.Abs(local),Math.Abs(preview))));
        double lateralSpeed=hasHeading?Math.Sin(CToolHeadingResolver.Difference(body,tool.Heading*180/Math.PI)*Math.PI/180)*heading.SpeedMetersPerSecond*(input.IsReverse?-1:1):0;
        double correction=controller.Update(r.Now,tool.Error,lateralSpeed,hasHeading,s.LookAhead,s.Interval,
            (int)s.Tracking,(int)s.Heading,(int)s.Acquire,(int)s.Strength,limit,s.EarlyRetry,Math.Abs(toolCurve)>0.001);
        // AgOpenWeb has no legacy curve gain: unchecked geometry means feedback-only.
        double curve=s.UseGeometry?geometry.Offset(0.5*local+0.5*preview)*Math.Clamp(s.CurveResponse,0,300)/100:0;
        if(!double.IsFinite(curve))curve=0;
        double dt=Math.Clamp((r.Now-last).TotalSeconds,0,0.5);last=r.Now;
        curveAverage+=(Math.Clamp(curve,-limit,limit)-curveAverage)*dt/(0.4+dt);
        correction=controller.ConstrainWithCurve(curveAverage,limit);
        double target=Math.Clamp(correction+curveAverage,-limit,limit),step=0.20*Math.Clamp(s.Strength,50,200)/100*dt;
        applied=Math.Clamp(applied+Math.Clamp(target-applied,-step,step),-limit,limit);
        var goal=Project(points,output.GoalPoint.Easting,output.GoalPoint.Northing,input.Track.IsClosed);
        double goalHeading=goal.Index>=0?goal.Heading:tractor.Heading;
        output.GoalPoint=new Vec2(output.GoalPoint.Easting+Math.Cos(goalHeading)*applied,
            output.GoalPoint.Northing-Math.Sin(goalHeading)*applied);
        return new(r.Requested,true,error,applied,heading.Source,Math.Abs(correction+curveAverage)>=limit-1e-6?"Offset limited":controller.CorrectionReason,east,north,true);
    }
    private static (int Index,double Error,double Heading,double Station) Project(IList<Vec3> p,double e,double n,bool closed)
    {
        double best=double.MaxValue,error=0,head=0,station=0;int index=-1;
        int count=closed?p.Count:p.Count-1;
        for(int i=0;i<count;i++)
        {
            var a=p[i];var b=p[(i+1)%p.Count];double dx=b.Easting-a.Easting,dn=b.Northing-a.Northing,len=dx*dx+dn*dn;
            if(!double.IsFinite(len)||len<1e-12)continue;
            double u=Math.Clamp(((e-a.Easting)*dx+(n-a.Northing)*dn)/len,0,1);
            double de=e-a.Easting-u*dx,dnn=n-a.Northing-u*dn,d2=de*de+dnn*dnn;
            if(d2>=best)continue;best=d2;index=i;head=Math.Atan2(dx,dn);station=u*Math.Sqrt(len);
            error=((e-a.Easting)*dn-(n-a.Northing)*dx)/Math.Sqrt(len);
        }
        return(index,error,head,station);
    }
}
