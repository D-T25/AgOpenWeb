// GNU GPL v3. Independent ToolDual (PANDA/PAOGI) input on UDP 19999.
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgOpenWeb.Models.GPS;
namespace AgOpenWeb.Services.Passive;
public sealed class ImplementGpsReceiver : IDisposable
{
    private readonly object lifecycle=new();
    private UdpClient? client;
    private ImplementGpsFix? latest;
    public ImplementGpsFix? Latest => Volatile.Read(ref latest);
    public string Error { get; private set; } = "";
    public void Start()
    {
        lock(lifecycle)
        {
        if(client!=null)return;
        try { var socket=new UdpClient(new IPEndPoint(IPAddress.Any,19999));client=socket;Error="";_ = Receive(socket); }
        catch(SocketException) { Error="Implement GPS port 19999 is unavailable"; }
        }
    }
    private async Task Receive(UdpClient socket)
    {
        try { while(true) { var packet=await socket.ReceiveAsync().ConfigureAwait(false);ProcessDatagram(packet.Buffer,DateTime.UtcNow); } }
        catch(ObjectDisposedException) { }
        catch(SocketException) { }
    }
    public void ProcessDatagram(ReadOnlySpan<byte> data,DateTime utc)
    {
        foreach(string line in Encoding.ASCII.GetString(data).Split('\n'))
            if(TryDecode(line.Trim(),utc,out var fix))Volatile.Write(ref latest,fix);
    }
    public static bool TryDecode(string line,DateTime utc,out ImplementGpsFix? fix)
    {
        fix=null;int star=line.IndexOf('*');
        if(!line.StartsWith('$')||star<1||star+3>line.Length)return false;
        int sum=0;for(int i=1;i<star;i++)sum^=line[i];
        if(!int.TryParse(line.AsSpan(star+1,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int sent)||sum!=sent)return false;
        var f=line[..star].Split(',');if(f.Length<16||(f[0]!="$PANDA"&&f[0]!="$PAOGI"))return false;
        double D(int i) => i<f.Length&&double.TryParse(f[i],NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?v:double.NaN;
        double lat=D(2),lon=D(4);if(!double.IsFinite(lat)||!double.IsFinite(lon)||lat<0||lon<0||lat%100>=60||lon%100>=60)return false;
        lat=Math.Floor(lat/100)+(lat%100)/60;lon=Math.Floor(lon/100)+(lon%100)/60;
        if(f[3]=="S")lat=-lat;else if(f[3]!="N")return false;
        if(f[5]=="W")lon=-lon;else if(f[5]!="E")return false;
        if(Math.Abs(lat)>90||Math.Abs(lon)>180)return false;
        double quality=D(6);if(!double.IsFinite(quality)||quality<0||quality>8)return false;
        bool dual=f[0]=="$PAOGI";double heading=D(12),roll=D(13),course=D(19);
        if(!dual){heading=heading==65535?double.NaN:heading/10;roll=roll/10;}
        DateTime ht=utc;
        if(f.Length>=20)
        {
            // ToolDual extensions: source (0 none/1 dual/2 relative IMU), valid, age ms, course.
            double source=D(16),valid=D(17),age=D(18);
            if(valid!=1||!double.IsFinite(age)||age<0||age>2000||source!=(dual?1:2))heading=double.NaN;
            else ht=utc.AddMilliseconds(-age);
        }
        if(heading<0||heading>360)heading=double.NaN;
        if(course<0||course>360)course=double.NaN;
        fix=new(utc,lat,lon,(int)quality,D(11)*0.514444,dual?heading:double.NaN,course,dual?double.NaN:heading,ht,roll);
        return true;
    }
    public void Dispose(){lock(lifecycle){client?.Dispose();client=null;Volatile.Write(ref latest,null);}}
}
