/*

mtf - a Microsoft Tape Format reader (and future writer?)
Copyright (C) 1999  D. Alan Stewart, Layton Graphics, Inc.

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.

Contact the author at:

D. Alan Stewart
Layton Graphics, Inc.
155 Woolco Dr.
Marietta, GA 30062, USA
astewart@layton-graphics.com

See mtf.c for version history, contributors, etc.

**
**    mtfutil.c
**
**    mtf utility functions
**
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public static class mtfutil {

    public class OffsetList {
        public Dictionary< Memory<byte>, int > regions = new Dictionary<Memory<byte>, int>();
        public Dictionary< Type, Tuple<Memory<byte>, int> > sources = new Dictionary<Type, Tuple<Memory<byte>, int>>();
    }

    public static void AddOrSet<K, V>(this Dictionary<K, V> dictionary, K key, V value)
    {
        if (!dictionary.ContainsKey(key))
            dictionary.Add(key, value);
        else
            dictionary[key] = value;
    }

    public static ref T access<T>(this OffsetList offsets, Memory<byte> buffer, int offset) where T : struct
    {
        var slice = buffer.Slice(0, offset);
        offsets.regions.AddOrSet(buffer, 0);
        offsets.regions.AddOrSet(slice, offset);
        offsets.sources.AddOrSet(typeof(T), Tuple.Create(slice, offset));
        return ref MemoryMarshal.AsRef<T>(slice.Span);
    }

    public static ref T access<T, B>(this OffsetList offsets, B buffer, int offset) where T : struct
    {
        var source = offsets.sources[typeof(B)];
        var slice = source.Item1.Slice(0, offset);
        var position = source.Item2 + offset;
        offsets.regions.AddOrSet(slice, position);
        offsets.sources.AddOrSet(typeof(T), Tuple.Create(slice, position));
        return ref MemoryMarshal.AsRef<T>(slice.Span);
    }

    public static int offsetOf(this OffsetList offsets, Memory<byte> buffer)
    {
        return offsets.regions[buffer];
    }

    public static int offsetOf<T>(this OffsetList offsets, T buffer)
    {
        return offsets.sources[typeof(T)].Item2;
    }

    public static Memory<byte> addrOf<T>(this OffsetList offsets, T buffer)
    {
        return offsets.sources[typeof(T)].Item1;
    }

    public static ArraySegment<byte> ReverseForBigEndian(this byte[] byteArray, int startIndex, int count)
    {
        var seg = new ArraySegment<byte>(byteArray, startIndex, count);
        if (!BitConverter.IsLittleEndian) return seg;
        var a = seg.ToArray();
        Array.Reverse(a);
        return a;
    }

    public static ArraySegment<byte> getbytes(System.UInt32 value) {
        return ReverseForBigEndian(System.BitConverter.GetBytes(value), 0, 4);
    }

    public static void memmove(Memory<byte> destination, Memory<byte> source, int offset, int size)
    {
        source.Slice(offset, size).CopyTo(destination);
    }

    public static void memcpy<T>(ref T destination, Memory<byte> source, int size) where T : struct
    {
        MemoryMarshal.TryRead(source.Slice(0, size).Span, out destination);
    }

    public static void memcopy<T>(ref T destination, int offset, Memory<byte> source, int size) where T : struct
    {
        var d = MemoryMarshal.Cast<T, byte>(MemoryMarshal.CreateSpan(ref destination, Unsafe.SizeOf<T>()));
        source.Span.CopyTo(d.Slice(offset, size - offset));
    }

    public static void dump(string fileName, Memory<byte> buffer, int size)
    {

        var handle = open(fileName, O_WRONLY | O_TRUNC | O_CREAT);
        if (handle.Item2 != null)
        {
            write(handle.Item1, buffer, 0, size);
            close(handle.Item1);
        }

        return;
    }

    public static int regcomp(ref Regex regex, string pattern, RegexOptions flags)
    {
        try {
            regex = new Regex(pattern, flags | RegexOptions.Compiled);
            return 0;
        } catch {
            return -1;
        }
    }

    public static int regexec(Regex regex, string str)
    {
        return regex.IsMatch(str) ? 0 : -1;
    }

    public const int stdout = 0;
    public const int stderr = 1;

    public static bool isalnum(byte c) {
        return Char.IsLetterOrDigit((Char)c);
    }

    public static void fprintf(int file, string format, params object[] args) {
        if (file == stdout) System.Console.Out.WriteLine(format, args);
        if (file == stderr) System.Console.Error.WriteLine(format, args);
    }


    public static void sprintf(out string dest, string format, params object[] args) {
        dest = System.String.Format(format, args);
    }

    public const int O_RDONLY = 1;
    public const int O_WRONLY = 2;
    public const int O_TRUNC = 4;
    public const int O_CREAT = 8;
    public const int S_IRWXU = 16;
    public const int S_IRWXG = 32;
    public const int S_IRWXO = 64;

    public static Tuple<FileStream, IOException> open(string fullPath, int flags) {
        return open(fullPath, flags, 0);
    }

    public static Tuple<FileStream, IOException> open(string fullPath, int flags, int permission)
    {
        try {
            if ((flags & O_RDONLY) != 0)
                return Tuple.Create(File.OpenRead(fullPath), (IOException)null);
            else if ((flags & O_WRONLY) != 0)
                return Tuple.Create(File.OpenWrite(fullPath), (IOException)null);
            else
                return Tuple.Create((FileStream)null, (IOException)null);
        } catch (IOException e) {
            return Tuple.Create((FileStream)null, e);
        }
    }

    public static int read(FileStream file, Memory<byte> buffer, int offset, int size)
    {
        return file.Read(buffer.Slice(offset, size).Span);
    }

    public static int write(FileStream file, Memory<byte> buffer, int offset, int size)
    {
        file.Write(buffer.Slice(offset, size).Span);
        return size;
    }

    public static IOException close(FileStream output)
    {
        try {
            output.Close();
            return null;
        } catch (IOException e) {
            return e;
        }
    }

    public struct mtop
    {
        public int mt_op;
        public int mt_count;
    }

    public struct mtget
    {
        public int mt_fileno;
        public long mt_blkno;
    }

    public const int MTNOP = 0x1000;
    public const int MTFSF = 0x2000;

    public const int MTIOCTOP = 1;
    public const int MTIOCGET = 2;

    public static int ioctl(FileStream mtfd, int flags, ref mtop mtop)
    {
        return 0;
    }

    public static int ioctl(FileStream mtfd, int flags, out mtget mtop)
    {
        mtop.mt_fileno = 0;
        mtop.mt_blkno = mtfd.Position;
        return 0;
    }


    public struct tm
    {
        public byte tm_sec;
        public byte tm_min;
        public byte tm_hour;
        public byte tm_mday;
        public byte tm_mon;
        public ushort tm_year;
    }

    public static DateTime mktime(tm tbuf)
    {
        return new DateTime(
            tbuf.tm_year,
            tbuf.tm_mon,
            tbuf.tm_mday,
            tbuf.tm_hour,
            tbuf.tm_min,
            tbuf.tm_sec);
    }

    public struct DateTimeBuffer
    {
        public DateTime Created;
        public DateTime Accessed;
    }

}
