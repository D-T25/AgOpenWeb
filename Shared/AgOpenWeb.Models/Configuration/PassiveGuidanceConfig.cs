// GNU GPL v3. Manual implement-guidance settings; deliberately no calibration.
namespace AgOpenWeb.Models.Configuration;
public sealed class PassiveGuidanceConfig
{
    public bool Enabled { get; set; }
    public double Tracking { get; set; } = 100;
    public double Heading { get; set; } = 100;
    public double Acquire { get; set; } = 100;
    public double CurveResponse { get; set; } = 100;
    public double Interval { get; set; } = 4;
    public double Strength { get; set; } = 100;
    public double StraightLimit { get; set; } = 1;
    public double CurveLimit { get; set; } = 1;
    public double LookAhead { get; set; }
    public bool EarlyRetry { get; set; } = true;
    public bool UseGeometry { get; set; }
    public double AntennaHeight { get; set; }
    public double AntennaOffset { get; set; }
    public double AntennaToWorkingPoint { get; set; }
    public double HeadingOffset { get; set; }
    public double RollZero { get; set; }
    public bool InvertRoll { get; set; }
}
