using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using RuinarchCoop;

class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name)
    {
        try { action(); } catch (IOException) { Check(true, name); return; } catch (InvalidDataException) { Check(true, name); return; }
        throw new Exception("Accepted invalid input: " + name);
    }
    static byte[] Zip(params string[] names)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var name in names) using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("example data");
        return ms.ToArray();
    }
    static void Main()
    {
        var save = Zip("mainSave.sav", "gameDB.db", "quickInfo.json", "screen.png");
        var frame = new MemoryStream(); Wire.SendArchive(frame, save); frame.Position = 0;
        Check(Wire.Hash(Wire.ReceiveArchive(frame)) == Wire.Hash(save), "archive roundtrip");
        var corrupt = frame.ToArray(); corrupt[4] ^= 1;
        Reject(() => Wire.ReceiveArchive(new MemoryStream(corrupt)), "checksum corruption");
        var truncated = frame.ToArray(); Array.Resize(ref truncated, truncated.Length - 2);
        Reject(() => Wire.ReceiveArchive(new MemoryStream(truncated)), "interrupted transfer");
        foreach (var n in new[] { -1, 0, Wire.MaxArchive + 1 })
        {
            var m = new MemoryStream(); Wire.WriteText(m, "hash"); Wire.WriteInt(m, n); m.Position = 0;
            Reject(() => Wire.ReceiveArchive(m), "archive length " + n);
        }
        var text = new MemoryStream(); Wire.WriteInt(text, 4097); text.Position = 0;
        Reject(() => Wire.ReadText(text), "oversized handshake field");
        Reject(() => Wire.ValidateArchive(Zip("../escape", "mainSave.sav")), "archive traversal");
        Reject(() => Wire.ValidateArchive(Zip("mainSave.sav", "mainSave.sav", "gameDB.db", "quickInfo.json")), "duplicate entry");
        Reject(() => Wire.ValidateArchive(Zip("mainSave.sav")), "missing database");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(() =>
        {
            using (var peer = listener.AcceptTcpClient())
            {
                peer.ReceiveTimeout = 5000; peer.SendTimeout = 5000;
                var s = peer.GetStream();
                if (Wire.ReadText(s) != "HELLO") throw new Exception("handshake");
                Wire.SendArchive(s, save);
                if (Wire.ReadText(s) != "READY") throw new Exception("ack");
            }
        });
        using (var client = new TcpClient("127.0.0.1", port))
        {
            client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
            Wire.WriteText(client.GetStream(), "HELLO");
            Check(Wire.Hash(Wire.ReceiveArchive(client.GetStream())) == Wire.Hash(save), "TCP socket world transfer");
            Wire.WriteText(client.GetStream(), "READY");
        }
        if (!server.Wait(6000)) throw new Exception("server timeout"); listener.Stop();
        Check(true, "TCP acknowledgement");
        Console.WriteLine(checks + " checks passed");
    }
}
