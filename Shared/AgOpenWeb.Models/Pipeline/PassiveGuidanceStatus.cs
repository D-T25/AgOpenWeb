// GNU GPL v3. Immutable cycle-to-UI telemetry.
namespace AgOpenWeb.Models.Pipeline;
public sealed record PassiveGuidanceStatus(bool Requested=false,bool Engaged=false,
    double ToolError=0,double Offset=0,string HeadingSource="Unavailable",string Reason="Off",
    double ToolEast=0,double ToolNorth=0,bool ToolValid=false);
