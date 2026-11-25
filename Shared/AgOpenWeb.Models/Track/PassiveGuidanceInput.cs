// GNU GPL v3. Immutable-per-cycle inputs to passive guidance.
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Models.GPS;
namespace AgOpenWeb.Models.Track;
public sealed class PassiveGuidanceInput
{
    public PassiveGuidanceConfig Settings { get; init; } = new();
    public DateTime Now { get; init; }
    public bool Requested { get; init; }
    public ImplementGpsFix? Fix { get; init; }
    public double ToolEast { get; init; }
    public double ToolNorth { get; init; }
    public bool HasLocalPlane { get; init; }
    public string Context { get; init; } = "";
    public string InputError { get; init; } = "";
    public bool Trailing { get; init; }
    public bool TowBetween { get; init; }
    public bool Front { get; init; }
    public bool Articulated { get; init; }
    public double Hitch { get; init; }
    public double Drawbar { get; init; }
    public double TankDrawbar { get; init; }
    public double WorkingBar { get; init; }
    public double Width { get; init; }
}
