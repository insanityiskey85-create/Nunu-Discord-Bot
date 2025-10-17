namespace NunuDiscordBot;

using System.Text;

public static class SimpleMidi
{
    public static Task WriteSingleTrackAsync(string path, string text, string mood)
    {
        var rnd = new Random(mood.GetHashCode());
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        void W(string s) => bw.Write(Encoding.ASCII.GetBytes(s));
        void BE16(ushort v) { bw.Write((byte)(v >> 8)); bw.Write((byte)(v & 0xFF)); }
        void BE32(uint v) { bw.Write((byte)(v >> 24)); bw.Write((byte)(v >> 16)); bw.Write((byte)(v >> 8)); bw.Write((byte)v); }

        W("MThd"); BE32(6); BE16(0); BE16(1); BE16(480);

        using var track = new MemoryStream();
        using var t = new BinaryWriter(track, Encoding.ASCII, leaveOpen: true);

        void VarLen(int v)
        {
            int buffer = v & 0x7F;
            while ((v >>= 7) > 0) { buffer <<= 8; buffer |= ((v & 0x7F) | 0x80); }
            while (true)
            {
                t.Write((byte)buffer);
                if ((buffer & 0x80) != 0) buffer >>= 8; else break;
            }
        }

        VarLen(0); t.Write((byte)0xFF); t.Write((byte)0x51); t.Write((byte)0x03); t.Write(new byte[] { 0x07, 0xA1, 0x20 });

        int time = 0;
        foreach (var ch in text.Take(64))
        {
            int pitch = 60 + (ch % 12);
            int vel = 64 + (rnd.Next(0, 32));
            int dur = 120 + (rnd.Next(0, 240));

            VarLen(time); t.Write((byte)0x90); t.Write((byte)pitch); t.Write((byte)vel);
            VarLen(dur); t.Write((byte)0x80); t.Write((byte)pitch); t.Write((byte)0);
            time = rnd.Next(30, 120);
        }

        VarLen(0); t.Write((byte)0xFF); t.Write((byte)0x2F); t.Write((byte)0x00);
        t.Flush();

        W("MTrk"); BE32((uint)track.Length);
        track.Position = 0; track.CopyTo(ms);

        File.WriteAllBytes(path, ms.ToArray());
        return Task.CompletedTask;
    }
}
