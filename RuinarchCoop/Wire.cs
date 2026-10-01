using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RuinarchCoop
{
    // No Unity calls here: the transport is also exercised by the standalone tests.
    internal static class Wire
    {
        internal const int Protocol = 1;
        internal const int MaxArchive = 128 * 1024 * 1024;
        internal static void WriteText(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 4096) throw new InvalidDataException("Text too long");
            WriteInt(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        internal static string ReadText(Stream stream)
        {
            int count = ReadInt(stream);
            if (count < 0 || count > 4096) throw new InvalidDataException("Invalid text size");
            return Encoding.UTF8.GetString(ReadExact(stream, count));
        }
        internal static void WriteInt(Stream stream, int n)
        {
            stream.WriteByte((byte)n); stream.WriteByte((byte)(n >> 8));
            stream.WriteByte((byte)(n >> 16)); stream.WriteByte((byte)(n >> 24));
        }
        internal static int ReadInt(Stream stream)
        {
            var b = ReadExact(stream, 4);
            return b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24;
        }
        internal static byte[] ReadExact(Stream stream, int count)
        {
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int n = stream.Read(result, offset, count - offset);
                if (n == 0) throw new EndOfStreamException("Connection ended during transfer");
                offset += n;
            }
            return result;
        }
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        internal static void SendArchive(Stream stream, byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > MaxArchive) throw new InvalidDataException("Archive too large or empty");
            WriteText(stream, Hash(bytes));
            WriteInt(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        internal static byte[] ReceiveArchive(Stream stream)
        {
            string hash = ReadText(stream);
            int length = ReadInt(stream);
            if (length <= 0 || length > MaxArchive) throw new InvalidDataException("Invalid archive size");
            var data = ReadExact(stream, length);
            if (Hash(data) != hash) throw new InvalidDataException("Archive checksum mismatch");
            ValidateArchive(data);
            return data;
        }
        internal static void ValidateArchive(byte[] bytes)
        {
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                long total = 0;
                foreach (var e in zip.Entries)
                {
                    // The game extracts directly. Permit only its four expected root files.
                    if (e.FullName != "mainSave.sav" && e.FullName != "gameDB.db" &&
                        e.FullName != "quickInfo.json" && e.FullName != "screen.png")
                        throw new InvalidDataException("Unexpected save archive entry: " + e.FullName);
                    if (!names.Add(e.FullName)) throw new InvalidDataException("Duplicate archive entry");
                    total += e.Length;
                    if (total > 512L * 1024 * 1024) throw new InvalidDataException("Expanded save too large");
                    // Force decompression now, before passing the archive to the game.
                    using (var input = e.Open())
                    {
                        var buffer = new byte[8192];
                        long actual = 0;
                        int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            actual += n;
                            if (actual > e.Length) throw new InvalidDataException("Invalid entry size");
                        }
                        if (actual != e.Length) throw new InvalidDataException("Truncated archive entry");
                    }
                }
                if (!names.Contains("mainSave.sav") || !names.Contains("gameDB.db") || !names.Contains("quickInfo.json"))
                    throw new InvalidDataException("Incomplete game save");
            }
        }
    }
}
