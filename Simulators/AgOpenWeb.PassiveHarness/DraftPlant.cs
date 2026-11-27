// GNU GPL v3. Independent test plant; never uses production tool-position/curve geometry.
namespace AgOpenWeb.PassiveHarness;
public record DraftPlantParameters(double MassKg=2500,double WidthM=8,double DepthM=3,double DrawbarM=8,
    double HitchM=-1.5,double WorkingBarM=1.5,double CorneringStiffnessNPerRad=24000,double Friction=.45,
    double LongitudinalDraftN=30000,double Asymmetry=.02,double SlopeDegrees=3,
    double HitchPlayM=.025,double SteeringLagS=.35,double TractorMassKg=9000,
    double TractorCorneringStiffnessNPerRad=240000,double TractorYawDampingNms=150000);
public record DraftSensors(double PositionSigmaM=.015,double ImplementHz=5,double ImplementDelayS=.15,
    double TractorDelayS=.10,double HeadingSigmaDegrees=.15,string Heading="dual",bool Dropout=false,double RollSigmaDegrees=.1,double SlowBiasM=.01,double ImuDriftDegPerSec=.003);
public record DraftCase(string Name,string Shape="straight",double RadiusM=40,string Terrain="uniform",
    DraftPlantParameters? Plant=null,DraftSensors? Sensors=null,double SpeedKph=7.2,double OffsetLimitM=2,
    bool Stress=false,bool Geometry=true,double ProfileDrawbarScale=1,double IntervalS=4);
public record DraftEnvironment(double SlopeDegrees,double GripFactor,double DraftFactor,double AsymmetryFactor);
public record DraftPose(double T,double E,double N,double H,double Beta,double ToolE,double ToolN,
    double ToolVE,double ToolVN,double Speed,double RollDegrees,double HitchE,double HitchN);

public sealed class DraftPlant
{
    public readonly DraftPlantParameters P;
    public double E,N,H,Beta,Speed,Steer,YawRate,BetaRate,Play,SoilForceN,DraftForceN,GravityForceN,HitchReactionN;
    public double Saturation,MaxConstraintError,TractorSlip;
    private double previousHitchVE,previousHitchVN;
    private double toolVE,toolVN,lastSlope;
    private bool initialized;
    public DraftPlant(DraftPlantParameters p,double speed,double startN=0)
    { P=p;Speed=speed;N=startN;previousHitchVN=speed; }
    public DraftPose Pose(double t)
    {
        double he=E+P.HitchM*Math.Sin(H)+Play*Math.Cos(H),hn=N+P.HitchM*Math.Cos(H)-Play*Math.Sin(H);
        double te=he-(P.DrawbarM+P.WorkingBarM)*Math.Sin(Beta),tn=hn-(P.DrawbarM+P.WorkingBarM)*Math.Cos(Beta);
        // World downhill is east; body roll is its component across the implement.
        double roll=Math.Atan(Math.Tan(lastSlope*Math.PI/180)*Math.Cos(Beta))*180/Math.PI;
        return new(t,E,N,H,Beta,te,tn,toolVE,toolVN,Speed,roll,he,hn);
    }
    public static DraftEnvironment Environment(DraftCase c,double t,double distance)
    {
        var p=c.Plant!;double ramp=Math.Clamp((t-20)/5,0,1),slope=p.SlopeDegrees;
        double patch=.5+.5*Math.Tanh(5*Math.Sin(distance/18));
        if(c.Terrain=="reversal")slope*=1-2*Math.Clamp((t-65)/8,0,1);
        if(c.Terrain=="rolling")slope*=Math.Sin(distance/40);
        return new(slope*ramp,c.Terrain=="patches"?1-.45*patch:1,
            c.Terrain=="patches"?1+.6*patch:1,ramp*(c.Terrain=="patches"?1+.8*patch:1));
    }
    public void Step(double dt,double demandedSteerDegrees,double targetSpeed,DraftEnvironment env)
    {
        const double g=9.80665,wheelbase=3;
        var before=Pose(0);lastSlope=env.SlopeDegrees;
        Speed+=Math.Clamp((targetSpeed-Speed)*dt/1.5,-.4*dt,.4*dt);
        double demanded=Math.Clamp(demandedSteerDegrees,-35,35)*Math.PI/180;
        Steer+=Math.Clamp((demanded-Steer)*dt/P.SteeringLagS,-30*Math.PI/180*dt,30*Math.PI/180*dt);
        double gravity=P.TractorMassKg*g*Math.Sin(env.SlopeDegrees*Math.PI/180)*Math.Cos(H);
        double reactionOnTractor=-HitchReactionN*Math.Cos(Beta-H)
            -P.LongitudinalDraftN*env.DraftFactor*Math.Sin(Beta-H);
        double slip=Math.Clamp((gravity+reactionOnTractor)/P.TractorCorneringStiffnessNPerRad,-.5,.5);
        double yawTarget=Speed/wheelbase*Math.Tan(Steer)+P.HitchM*reactionOnTractor/P.TractorYawDampingNms;
        YawRate+=(yawTarget-YawRate)*dt/.25;
        double ve=Speed*Math.Sin(H)+slip*Math.Cos(H),vn=Speed*Math.Cos(H)-slip*Math.Sin(H);
        // Compliance/freeplay proxy: finite travel and rate, not an unmodelled instantaneous jump.
        double playTarget=-Math.Sign(HitchReactionN)*P.HitchPlayM;
        double playRate=Math.Clamp((playTarget-Play)/.25,-.10,.10);
        Play+=playRate*dt;
        double hve=ve+P.HitchM*YawRate*Math.Cos(H)+playRate*Math.Cos(H)-Play*YawRate*Math.Sin(H);
        double hvn=vn-P.HitchM*YawRate*Math.Sin(H)-playRate*Math.Sin(H)-Play*YawRate*Math.Cos(H);
        double hae=initialized?(hve-previousHitchVE)/dt:0,han=initialized?(hvn-previousHitchVN)/dt:0;
        previousHitchVE=hve;previousHitchVN=hvn;initialized=true;
        double lateralV=hve*Math.Cos(Beta)-hvn*Math.Sin(Beta)-P.DrawbarM*BetaRate;
        double forceLimit=P.Friction*P.MassKg*g*Math.Cos(env.SlopeDegrees*Math.PI/180)*env.GripFactor;
        double drag=P.CorneringStiffnessNPerRad*env.GripFactor;
        double forwardV=hve*Math.Sin(Beta)+hvn*Math.Cos(Beta);
        double slipAngle=Math.Atan2(lateralV,Math.Max(.3,Math.Abs(forwardV)));
        SoilForceN=-forceLimit*Math.Tanh(drag*slipAngle/forceLimit);
        Saturation=Math.Abs(SoilForceN)/forceLimit;
        GravityForceN=P.MassKg*g*Math.Sin(env.SlopeDegrees*Math.PI/180)*Math.Cos(Beta);
        DraftForceN=P.LongitudinalDraftN*env.DraftFactor*P.Asymmetry*env.AsymmetryFactor;
        double inertia=P.MassKg*(P.WidthM*P.WidthM+P.DepthM*P.DepthM)/12;
        double hitchA=hae*Math.Cos(Beta)-han*Math.Sin(Beta);
        // Force/torque balance after eliminating hitch lateral reaction:
        // (I+mL²) beta'' = mL Ah_perp - L(Fsoil+Fg) - (L+W) Fdraft.
        double betaAcceleration=(P.MassKg*P.DrawbarM*hitchA-P.DrawbarM*(SoilForceN+GravityForceN)
            -(P.DrawbarM+P.WorkingBarM)*DraftForceN)/(inertia+P.MassKg*P.DrawbarM*P.DrawbarM);
        HitchReactionN=P.MassKg*(hitchA-P.DrawbarM*betaAcceleration)-SoilForceN-GravityForceN-DraftForceN;
        BetaRate+=betaAcceleration*dt;Beta+=BetaRate*dt;H+=YawRate*dt;E+=ve*dt;N+=vn*dt;
        var after=Pose(0);toolVE=(after.ToolE-before.ToolE)/dt;toolVN=(after.ToolN-before.ToolN)/dt;
        double axleE=after.HitchE-P.DrawbarM*Math.Sin(Beta),axleN=after.HitchN-P.DrawbarM*Math.Cos(Beta);
        MaxConstraintError=Math.Max(MaxConstraintError,Math.Abs(Math.Sqrt(Math.Pow(after.HitchE-axleE,2)+Math.Pow(after.HitchN-axleN,2))-P.DrawbarM));
    }
}
