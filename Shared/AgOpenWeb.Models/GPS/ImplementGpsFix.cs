// GNU GPL v3.
namespace AgOpenWeb.Models.GPS;
public sealed record ImplementGpsFix(DateTime ReceivedUtc,double Latitude,double Longitude,
    int FixQuality,double Speed,double DualHeading,double Course,double ImuHeading,
    DateTime HeadingUtc,double Roll);
