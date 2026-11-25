// GNU GPL v3.
using AgOpenWeb.Models.Configuration;
namespace AgOpenWeb.Services.Passive;
/// <summary>Converts the simulated working-bar position to a virtual antenna position.</summary>
public static class ToolGpsSimulator
{
    public static (double East,double North) AntennaPosition(double east,double north,double heading,PassiveGuidanceConfig settings)
        => (east-Math.Cos(heading)*settings.AntennaOffset+Math.Sin(heading)*settings.AntennaToWorkingPoint,
            north+Math.Sin(heading)*settings.AntennaOffset+Math.Cos(heading)*settings.AntennaToWorkingPoint);
}
