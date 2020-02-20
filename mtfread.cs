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
**    mtfread.c
**
**    functions for reading an MTF tape
**
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using static mtfutil;

public class mtfread : imtf {

    public string outPath;
    public string curPath;
    public FileStream mtfd = null;
    public Byte verbose, debug, list, forceCase;
    public Memory<Byte> tBuffer = new Memory<Byte>(new byte[MAX_TAPE_BLOCK_SIZE]);
    public UInt16 matchCnt;
    public UInt16 tapeBlockSize;
    public UInt32 minFree;
    public Regex[] match = new Regex[MAX_PATTERN];


    private Boolean compressPossible;
    private Boolean filemark;
    private UInt16 flbSize = 0;
    protected Int32 remaining;
    private UInt16 setCompress;
    private UInt32 blockCnt;


    private MTF_DB_HDR dbHdr;
    private MTF_TAPE_BLK tape;
    private MTF_SSET_BLK sset;
    private MTF_VOLB_BLK volb;
    private MTF_DIRB_BLK dirb;
    private MTF_FILE_BLK file;
    private MTF_CFIL_BLK cfil;
    private MTF_ESPB_BLK espb;
    private MTF_ESET_BLK eset;
    private MTF_EOTM_BLK eotm;
    private MTF_SFMB_BLK sfmb;
    private MTF_STREAM_HDR stream;

    private OffsetList offsets = new OffsetList();

    public ref T access<T>(Memory<byte> buffer, int offset) where T : struct
    {
        return ref offsets.access<T>(buffer, offset);
    }

    public int offsetOf(Memory<byte> buffer)
    {
        return offsets.offsetOf(buffer);
    }

    public int offsetOf<T>(T buffer)
    {
        return offsets.offsetOf<T>(buffer);
    }

    public Memory<byte> addrOf<T>(T buffer)
    {
        return offsets.addrOf<T>(buffer);
    }

    public ref T access<T>(MTF_DB_HDR buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_DB_HDR>(buffer, offset);
    }

    public ref T access<T>(MTF_TAPE_BLK buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_TAPE_BLK>(buffer, offset);
    }

    public ref T access<T>(MTF_SSET_BLK buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_SSET_BLK>(buffer, offset);
    }

    public ref T access<T>(MTF_VOLB_BLK buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_VOLB_BLK>(buffer, offset);
    }

    public ref T access<T>(MTF_DIRB_BLK buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_DIRB_BLK>(buffer, offset);
    }

    public ref T access<T>(MTF_FILE_BLK buffer, int offset) where T : struct
    {
        return ref offsets.access<T, MTF_FILE_BLK>(buffer, offset);
    }

    /* openMedia() reads the MTF tape header and prepares for reading the first   */
    /* data set.                                                                  */

    public override Int32 openMedia()
    {
        Int32 result;

        if (verbose > 0) fprintf(stdout, "\nReading TAPE block...\n");

        blockCnt = 0;
        filemark = false;

        result = readNextBlock(0);
        if (result != 0)
        {
            fprintf(stderr, "Error reading first block!\n");
            return(-1);
        }

        dbHdr = access<MTF_DB_HDR>(tBuffer, 0);

        if (dbHdr.type != MTF_TAPE)
        {
            fprintf(stderr, "Error reading first block of tape!\n");
            return(-1);
        }

        tape = access<MTF_TAPE_BLK>(dbHdr, 0);

        if (readTapeBlock() != 0)
        {
            fprintf(stderr, "Error reading TAPE block!\n");
            return(-1);
        }

        return(0);
    }


    /* readDataSet() reads a MTF data set.                                        */

    public override Int32 readDataSet()
    {
        Int32 result;

        if (verbose > 0) fprintf(stdout, "\nReading SSET block...\n");

        filemark = false;

        var dbHdr = access<MTF_DB_HDR>(tBuffer, 0);

        if (dbHdr.type != MTF_SSET)
        {
            var ptr = getbytes(sset.common.type);
            fprintf(stderr, "Unexpected descriptor block type \'%c%c%c%c\'!\n",
                    ptr[0], ptr[1], ptr[2], ptr[3]);
            return(-1);
        }

        sset = access<MTF_SSET_BLK>(dbHdr, 0);

        if (readStartOfSetBlock() != 0)
        {
            fprintf(stderr, "Error reading SSET block!\n");
            return(-1);
        }

        result = 0;
        while ((result == 0) && (!filemark))
        {
            dbHdr = access<MTF_DB_HDR>(tBuffer, 0);

            switch (dbHdr.type)
            {
                case MTF_VOLB:
                    if (verbose > 0) fprintf(stdout, "\nReading VOLB block...\n");
                    volb = access<MTF_VOLB_BLK>(dbHdr, 0);
                    result = readVolumeBlock();
                    break;

                case MTF_DIRB:
                    if (verbose > 0) fprintf(stdout, "\nReading DIRB block...\n");
                    dirb = access<MTF_DIRB_BLK>(dbHdr, 0);
                    result = readDirectoryBlock();
                    break;

                case MTF_FILE:
                    if (verbose > 0) fprintf(stdout, "\nReading FILE block...\n");
                    file = access<MTF_FILE_BLK>(dbHdr, 0);
                    result = readFileBlock();
                    break;

                case MTF_CFIL:
                    if (verbose > 0) fprintf(stdout, "\nReading CFIL block...\n");
                    cfil = access<MTF_CFIL_BLK>(dbHdr, 0);
                    result = readCorruptObjectBlock();
                    break;

                case MTF_ESPB:
                    if (verbose > 0) fprintf(stdout, "\nReading ESPB block...\n");
                    espb = access<MTF_ESPB_BLK>(dbHdr, 0);
                    result = readEndOfSetPadBlock();
                    break;

                case MTF_EOTM:
                    if (verbose > 0) fprintf(stdout, "\nReading EOTM block...\n");
                    eotm = access<MTF_EOTM_BLK>(dbHdr, 0);
                    result = readEndOfTapeMarkerBlock();
                    return(-1);

                case MTF_SFMB:
                    if (verbose > 0) fprintf(stdout, "\nReading SFMB block...\n");
                    sfmb = access<MTF_SFMB_BLK>(dbHdr, 0);
                    result = readSoftFileMarkBlock();
                    return(-1);

                default:
                    if (verbose > 1)
                    {
                        var ptr = getbytes(dbHdr.type);
                        if ((isalnum(ptr[0])) && (isalnum(ptr[1])) &&
                            (isalnum(ptr[2])) && (isalnum(ptr[3])))
                        {
                            fprintf(stdout,
                                    "Skipping descriptor block type \'%c%c%c%c\'!\n",
                                    ptr[0], ptr[1], ptr[2], ptr[3]);
                        }
                        else
                        {
                            fprintf(stdout,
                                    "Looking for next  descriptor block...\n");
                        }
                    }

                    result = readNextBlock(flbSize);
                    break;
            }
        }

        if (result < 0)
        {
            fprintf(stderr, "Error reading tape block!\n");
            return(-1);
        }
        else if (!filemark)
        {
            fprintf(stderr, "Error reading filemark!\n");
            return(-1);
        }
        else
        {
            result = readNextBlock(0);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(0);
    }


    /* readEndOfDataSet() reads the MTF end of set block and prepares for reading */
    /* the next data set. It returns 1 if there is none, 0 if there is.           */

    public override Int32 readEndOfDataSet()
    {
        Int32 result;

        if (verbose > 0) fprintf(stdout, "\nReading ESET block...\n");

        filemark = false;

        dbHdr = access<MTF_DB_HDR>(tBuffer, 0);

        if (dbHdr.type != MTF_ESET)
        {
            var ptr = getbytes(eset.common.type);
            fprintf(stderr, "Unexpected descriptor block type \'%c%c%c%c\'!\n",
                    ptr[0], ptr[1], ptr[2], ptr[3]);
            return(-1);
        }

        eset = access<MTF_ESET_BLK>(dbHdr, 0);

        result = readNextBlock(0);
        while (result == 0)
        {
            result = readNextBlock(0);
        }

        if (result < 0)
        {
            fprintf(stderr, "Error reading tape block!\n");
            return(-1);
        }

        if (!filemark)
        {
            fprintf(stderr, "Error reading filemark!\n");
            return(-1);
        }

        filemark = false;

        result = readNextBlock(0);
        if (result < 0)
        {
            fprintf(stderr, "Error reading tape block!\n");
            return(-1);
        }

        return(result);
    }


    /* readTapeBlock() reads an MTF TAPE descriptor block, advances past the      */
    /* following filemark and reads the first tape block of the first data set.   */

    public override Int32 readTapeBlock()
    {
        Int32 result;

        if (tape.ver != 1)
        {
            fprintf(stderr, "Unexpected MTF major version!\n");
            return(-1);
        }

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "TAPE Block Attributes: %08lX\n", tape.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);

            fprintf(stdout, "MTF Major Version: %u\n", tape.ver);
            fprintf(stdout, "Format Logical Block Size: %u\n", tape.flbSize);
            fprintf(stdout, "Media Family ID: %lu\n", tape.famId);
            fprintf(stdout, "Media Sequence Number: %u\n", tape.seq);
            fprintf(stdout, "Software Vendor ID: %u\n", tape.vendorId);
        }

        if (verbose > 0)
        {
            string ptr;

            ptr = getString(dbHdr.strType, tape.name.size,
                            tape, tape.name.offset);
            fprintf(stdout, "Media Name: %s\n", ptr);

            ptr = getString(dbHdr.strType, tape.desc.size,
                            tape, tape.desc.offset);
            fprintf(stdout, "Media Description: %s\n", ptr);

            ptr = getString(dbHdr.strType, tape.software.size,
                            tape, tape.software.offset);
            fprintf(stdout, "Software: %s\n", ptr);
        }

        flbSize = tape.flbSize;

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(tape, dbHdr.off);
            result = skipToNextBlock();
            if (result != 1)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result != 1)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        if (tapeBlockSize < flbSize)
        {
            if (readNextBlock(0) != 1)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        /* read first block past filemark */
        if (readNextBlock(0) != 0)
        {
            fprintf(stderr, "Error reading tape block!\n");
            return(-1);
        }

        return(0);
    }


    public override Int32 readStartOfSetBlock()
    {
        Int32 result;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "SSET Block Attributes: %08lX\n", sset.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);

            fprintf(stdout, "MTF Minor Version: %u\n", sset.ver);
            fprintf(stdout, "Software Vendor: %u\n", sset.vendor);
            fprintf(stdout, "Software Major Version: %u\n", sset.major);
            fprintf(stdout, "Software Minor Version: %u\n", sset.minor);
            fprintf(stdout, "Data Set Number: %u\n", sset.num);
            fprintf(stdout, "Physical Block Address: %luu\n", sset.pba);
            fprintf(stdout, "Password Encryption: %u\n", sset.passEncrypt);
            fprintf(stdout, "Software Compression: %u\n", sset.softCompress);
        }

        if (verbose > 0)
        {
            string ptr;

            ptr = getString(dbHdr.strType, sset.name.size,
                            sset, sset.name.offset);
            fprintf(stdout, "Data Set Name: %s\n", ptr);

            ptr = getString(dbHdr.strType, sset.desc.size,
                            sset, sset.desc.offset);
            fprintf(stdout, "Data Set Description %s\n", ptr);

            ptr = getString(dbHdr.strType, sset.user.size,
                            sset, sset.user.offset);
            fprintf(stdout, "User Name: %s\n", ptr);
        }

        setCompress = sset.softCompress;

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(sset, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(result);
    }


    public override Int32 readVolumeBlock()
    {
        Int32 result;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (verbose > 0)
        {
            string ptr;

            ptr = getString(dbHdr.strType, volb.device.size,
                            volb, volb.device.offset);
            fprintf(stdout, "Device Name: %s\n", ptr);

            ptr = getString(dbHdr.strType, volb.volume.size,
                            volb, volb.volume.offset);
            fprintf(stdout, "Volume Name: %s\n", ptr);

            ptr = getString(dbHdr.strType, volb.machine.size,
                            volb, volb.machine.offset);
            fprintf(stdout, "Machine Name: %s\n", ptr);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(volb, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(result);
    }


    public override Int32 readDirectoryBlock()
    {
        Int32 result;
        string ptr; string fullPath;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);

            fprintf(stdout, "Directory ID: %lu\n", dirb.id);
        }

        if ((dirb.attr & MTF_DIR_PATH_IN_STREAM_BIT) == 0)
        {
            ptr = getString(dbHdr.strType, dirb.name.size,
                            dirb, dirb.name.offset);

            curPath = ptr;

            if (verbose > 0) fprintf(stdout, "Directory Name: %s\n", ptr);

            if (forceCase == CASE_LOWER)
                curPath = curPath.ToLowerInvariant();
            else if (forceCase == CASE_UPPER)
                curPath = curPath.ToUpperInvariant();

            sprintf(out fullPath, "%s/%s", outPath, curPath);

            if ((list == 0) && (matchCnt == 0))
            {
                if (!File.Exists(fullPath))
                {
                    try
                    {
                        var ix = fullPath.LastIndexOf('/');
                        var tmpPath = fullPath.Substring(0, ix);
                        var str2 = fullPath.Substring(ix);
                        try {

                            var res = Directory.Exists(tmpPath);
                            while (!res)
                            {
                                if (debug > 0) fprintf(stderr, "%s did not exist\n", tmpPath);
                                if (res) break;

                                ix = fullPath.LastIndexOf('/', ix);
                                tmpPath = fullPath.Substring(0, ix);

                                res = Directory.Exists(tmpPath);
                            }
                        }

                        catch (Exception e)
                        {
                            fprintf(stderr, "Error %d testing for status of %s!\n",
                                    e, tmpPath);
                            return(-1);
                        }

                        while (tmpPath.Length < str2.Length)
                        {
                            tmpPath = fullPath.Substring(0, ix + 1);

                            if (debug > 0) fprintf(stderr, "creating %s...\n", tmpPath);
                            try {
                                Directory.CreateDirectory(tmpPath);
                            }

                            catch (IOException)
                            {
                                fprintf(stderr,
                                        "Unable to create directory %s!\n",
                                        tmpPath);
                                return(-1);
                            }


                            while (fullPath[ix] != '/') ix += 1;
                        }

                        sprintf(out fullPath, "%s/%s", outPath, curPath);
                    }
                    catch (IOException e)
                    {
                        fprintf(stderr, "Error %d testing for status of %s!\n",
                                e, fullPath);
                        return(-1);
                    }
                }
                else
                {
                    fprintf(stderr, "%s is not a directory!\n", fullPath);
                    return(-1);

                } /* if (!File.Exists(fullPath)) */

                if (verbose > 0)
                    fprintf(stdout, "Current path changed to %s\n", fullPath);

            } /* if ((list == 0) && (matchCnt == 0)) */
        }
        else
        {
            /* not implemented */
            fprintf(stderr, "Reading from stream not implemented!\n");
            return(-1);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result != 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(result);
    }


    public override Int32 readFileBlock()
    {
        Int32 result;
        string ptr; string filePath; string fullPath;
        int i; FileStream output = null;
        tm tbuf;
        DateTimeBuffer utbuf;
        UInt32 threshold;
        Tuple<FileStream, IOException> outp;
        IOException errno;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);

            fprintf(stdout, "Directory ID: %lu\n", file.dirId);
            fprintf(stdout, "File ID: %lu\n", file.id);

            fprintf(stdout, "Modification Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                    MTF_HOUR(file.mod), MTF_MINUTE(file.mod),
                    MTF_SECOND(file.mod), MTF_MONTH(file.mod), MTF_DAY(file.mod),
                    MTF_YEAR(file.mod));
            fprintf(stdout, "Creation Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                    MTF_HOUR(file.create), MTF_MINUTE(file.create),
                    MTF_SECOND(file.create), MTF_MONTH(file.create),
                    MTF_DAY(file.create), MTF_YEAR(file.create));
            fprintf(stdout, "Backup Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                    MTF_HOUR(file.backup), MTF_MINUTE(file.backup),
                    MTF_SECOND(file.backup), MTF_MONTH(file.backup),
                    MTF_DAY(file.backup), MTF_YEAR(file.backup));
            fprintf(stdout, "Access Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                    MTF_HOUR(file.access), MTF_MINUTE(file.access),
                    MTF_SECOND(file.access), MTF_MONTH(file.access),
                    MTF_DAY(file.access), MTF_YEAR(file.access));
        }

        tbuf.tm_sec = MTF_SECOND(file.mod);
        tbuf.tm_min = MTF_MINUTE(file.mod);
        tbuf.tm_hour = MTF_HOUR(file.mod);
        tbuf.tm_mday = MTF_DAY(file.mod);
        tbuf.tm_mon = (byte)(MTF_MONTH(file.mod) - 1);
        tbuf.tm_year = (ushort)(MTF_YEAR(file.mod) - 1900);

        utbuf.Created = mktime(tbuf);

        tbuf.tm_sec = MTF_SECOND(file.access);
        tbuf.tm_min = MTF_MINUTE(file.access);
        tbuf.tm_hour = MTF_HOUR(file.access);
        tbuf.tm_mday = MTF_DAY(file.access);
        tbuf.tm_mon = (byte)(MTF_MONTH(file.access) - 1);
        tbuf.tm_year = (ushort)(MTF_YEAR(file.access) - 1900);

        utbuf.Accessed = mktime(tbuf);

        if ((dbHdr.attr & MTF_COMPRESSION) != 0)
            compressPossible = true;
        else
            compressPossible = false;

        if ((file.attr & MTF_FILE_CORRUPT_BIT) != 0)
        {
            if (verbose > 0) fprintf(stdout, "File is corrupted... skipping!\n");

            stream = access<MTF_STREAM_HDR>(file, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr,
                        "Error traversing to end of file!\n");
                return(-1);
            }

            return(0);
        }

        if ((file.attr & MTF_FILE_NAME_IN_STREAM_BIT) == 0)
        {
            ptr = getString(dbHdr.strType, file.name.size,
                            file, file.name.offset);

            if (verbose > 0) fprintf(stdout, "File Name: %s\n", ptr);
        }
        else
        {
            /* not implemented */
            fprintf(stderr, "Reading from stream not implemented!\n");
            return(-1);
        }

        sprintf(out filePath, "%s%s", curPath, ptr);

        if (forceCase == CASE_LOWER)
            filePath = filePath.ToLowerInvariant();
        else if (forceCase == CASE_UPPER)
            filePath = filePath.ToUpperInvariant();

        sprintf(out fullPath, "%s/%s", outPath, filePath);

        if (matchCnt > 0)
        {
            i = 0;
            while ((i >= 0) && (i < matchCnt))
            {
                if (regexec(match[i], filePath) != 0)
                    i += 1;
                else
                    i = -1;
            }

            if (i >= 0)
            {
                if (verbose > 0)
                    fprintf(stdout, "%s does not match any patterns... skipping!\n",
                            filePath);

                stream = access<MTF_STREAM_HDR>(file, dbHdr.off);
                result = skipToNextBlock();
                if (result < 0)
                {
                    fprintf(stderr,
                            "Error traversing to end of %s!\n", fullPath);
                    return(-1);
                }

                return(0);
            }
        }

        if (list == 0)
        {
            if (verbose > 0)
                fprintf(stdout, "File will be written to %s\n", fullPath);
            else
                fprintf(stdout, "%s\n", fullPath);

            if (matchCnt > 0)
            {
                var ix = fullPath.LastIndexOf('/');
                var tmpPath = fullPath.Substring(0, ix);
                var str2 = fullPath.Substring(ix);
                try {

                    var res = Directory.Exists(tmpPath);
                    while (!res)
                    {
                        if (debug > 0) fprintf(stderr, "%s did not exist\n", tmpPath);
                        if (res) break;

                        ix = fullPath.LastIndexOf('/', ix);
                        tmpPath = fullPath.Substring(0, ix);

                        res = Directory.Exists(tmpPath);
                    }
                }

                catch (Exception e)
                {
                    fprintf(stderr, "Error %d testing for status of %s!\n",
                            e, tmpPath);
                    return(-1);
                }

                while (tmpPath.Length < str2.Length)
                {
                    tmpPath = fullPath.Substring(0, ix + 1);

                    if (debug > 0) fprintf(stderr, "creating %s...\n", tmpPath);
                    try {
                        Directory.CreateDirectory(tmpPath);
                    }

                    catch (IOException)
                    {
                        fprintf(stderr,
                                "Unable to create directory %s!\n",
                                tmpPath);
                        return(-1);
                    }


                    while (fullPath[ix] != '/') ix += 1;
                }
            }

            if (minFree != 0)
            {
                var ix = fullPath.LastIndexOf('/');
                filePath = fullPath.Substring(0, ix);

                var drives = DriveInfo.GetDrives();
                DriveInfo drive = null;
                foreach (var d in drives) {
                    if (filePath.StartsWith(d.Name)) drive = d;
                }

                if (drive == null || !drive.IsReady)
                {
                    if (debug > 0) fprintf(stderr, "filePath=%s\n", filePath);
                    fprintf(stderr, "Error testing for free space!\n");
                    return(-1);
                }

                threshold = (minFree + (UInt32) drive.TotalSize - 1) /
                    (UInt32) drive.TotalSize;

                if (debug > 0)
                    fprintf(stderr, "threshold=%lu avail=%ld\n", threshold, drive.AvailableFreeSpace);

                while ((UInt32) drive.AvailableFreeSpace < threshold)
                {
                    fprintf(stderr, "Free space is only %ld bytes!\n",
                            drive.AvailableFreeSpace * drive.TotalSize);
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(60));

                    if (drive == null || !drive.IsReady)
                    {
                        fprintf(stderr, "Error testing for free space!\n");
                        return(-1);
                    }

                    if (debug > 0) fprintf(stderr, "avail=%ld\n", drive.AvailableFreeSpace);
                }
            }

            outp = open(fullPath, O_WRONLY | O_TRUNC | O_CREAT,
                        S_IRWXU | S_IRWXG | S_IRWXO);
            if (outp.Item2 != null)
            {
                fprintf(stderr, "Error %d opening/creating %s for writing!\n",
                        outp.Item2, fullPath);
                return(-1);
            }
            output = outp.Item1;

        }
        else
        {
            fprintf(stdout, "%s\n", fullPath);
        }

        stream = access<MTF_STREAM_HDR>(file, dbHdr.off);

        if (list == 0)
        {
            while ((stream.id != MTF_STAN) && (stream.id != MTF_SPAD))
            {
                result = skipOverStream();
                if (result < 0)
                {
                    fprintf(stderr, "Error traversing stream!\n");
                    return(-1);
                }

                stream = access<MTF_STREAM_HDR>(tBuffer, result);
            }

            if (stream.id == MTF_STAN)
            {
                if (verbose > 1) fprintf(stdout, "Reading STAN stream...\n");

                result = writeData(output);
                if (result < 0)
                {
                    fprintf(stderr, "Error writing stream to file!\n");
                    return(-1);
                }

                if (debug > 0)
                    fprintf(stderr, "writeData() returned %ld\n", result);

                while ((result % flbSize) != 0)
                {
                    stream = access<MTF_STREAM_HDR>(tBuffer, result);
                    result = skipOverStream();
                    if (result < 0)
                    {
                        fprintf(stderr, "Error traversing stream!\n");
                        return(-1);
                    }

                    if (debug > 0)
                        fprintf(stderr, "skipOverStream() returned %ld\n", result);
                }
            }
            else
            {
                result = skipOverStream();
                if (result < 0)
                {
                    fprintf(stderr, "Error traversing stream!\n");
                    return(-1);
                }
            }
        }
        else
        {
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end %s!\n", fullPath);
                return(-1);
            }
        }

        if (list == 0)
        {
            if ((errno = close(output)) != null)
            {
                fprintf(stderr,
                        "Error %d closing %s!\n", errno, fullPath);
                return(-1);
            }

            try {
                File.SetCreationTimeUtc(fullPath, utbuf.Created);
                File.SetLastAccessTimeUtc(fullPath, utbuf.Accessed);
            }
            catch (IOException e)
            {
                fprintf(stderr,
                        "Error %d access<etting>/access time of %s!\n",
                        e, fullPath);

                if (debug > 0)
                {
                    fprintf(stderr, "Modification Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                            MTF_HOUR(file.mod), MTF_MINUTE(file.mod),
                            MTF_SECOND(file.mod), MTF_MONTH(file.mod),
                            MTF_DAY(file.mod),
                            MTF_YEAR(file.mod));
                    fprintf(stderr,"Access Date: %02u:%02u:%02u %02u/%02u/%04u\n",
                            MTF_HOUR(file.access), MTF_MINUTE(file.access),
                            MTF_SECOND(file.access), MTF_MONTH(file.access),
                            MTF_DAY(file.access), MTF_YEAR(file.access));
                }
            }
        }

        return(0);
    }


    public override Int32 readCorruptObjectBlock()
    {
        Int32 result;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(result);
    }


    public override Int32 readEndOfSetPadBlock()
    {
        Int32 result;

        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            result = skipToNextBlock();
            if (result < 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            result = readNextBlock(dbHdr.off);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(result);
    }


    public override Int32 readEndOfSetBlock()
    {
        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            if (skipToNextBlock() != 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            if (readNextBlock(dbHdr.off) != 1)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(0);
    }


    public override Int32 readEndOfTapeMarkerBlock()
    {
        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            if (skipToNextBlock() != 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            if (readNextBlock(dbHdr.off) != 1)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(0);
    }


    public override Int32 readSoftFileMarkBlock()
    {
        if (verbose > 1)
        {
            fprintf(stdout, "Descriptor Block Attributes: %08lX\n", dbHdr.attr);
            fprintf(stdout, "Format Logical Address: %luu\n", dbHdr.fla);
            fprintf(stdout, "Offset To First Event: %u\n", dbHdr.off);
            fprintf(stdout, "String Type: %u\n", dbHdr.strType);
            fprintf(stdout, "OS: %u\n", dbHdr.osId);
        }

        if (dbHdr.off < flbSize)
        {
            stream = access<MTF_STREAM_HDR>(dirb, dbHdr.off);
            if (skipToNextBlock() != 0)
            {
                fprintf(stderr, "Error traversing to end of descriptor block!\n");
                return(-1);
            }
        }
        else
        {
            if (readNextBlock(dbHdr.off) != 1)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        return(0);
    }


    /* readNextBlock() keeps track of how many logical blocks have been used from */
    /* the last tape block read. If needed, it reads another tape block in. The   */
    /* global variable, remaining, is used to track how much data remains in the  */
    /* block buffer.                                                              */

    public override Int32 readNextBlock(Int32 advance)
    {
        int result;
        mtop op;
        mtget get;

        if (debug > 0) fprintf(stderr, "advance=%u remaining=%u\n", advance, remaining);

        if ((advance == 0) || (remaining == 0) || (advance == remaining))
        {
            if (debug > 0)
            {
                op.mt_op = MTNOP;
                op.mt_count = 0;

                if (ioctl(mtfd, MTIOCTOP, ref op) != 0)
                {
                    fprintf(stderr, "Error returned by MTIOCTOP!\n");
                    return(-1);
                }

                if (ioctl(mtfd, MTIOCGET, out get) != 0)
                {
                    fprintf(stderr, "Error returned by MTIOCGET!\n");
                    return(-1);
                }

                fprintf(stderr, "tape file no. %u\n", get.mt_fileno);
                fprintf(stderr, "tape block no. %u\n", get.mt_blkno);
            }

            remaining = 0;

            if (flbSize == 0) /* first block of tape, don't know flbSize yet */
            {
                if (tapeBlockSize == 0)
                    result = read(mtfd, tBuffer, 0, MAX_TAPE_BLOCK_SIZE);
                else
                    result = read(mtfd, tBuffer, 0, tapeBlockSize);

                if (result < 0)
                {
                    fprintf(stderr, "Error reading tape!\n");
                    return(-1);
                }

                remaining = result;

                if (tapeBlockSize == 0)
                {
                    tapeBlockSize = (UInt16) result;

                    if (verbose > 0)
                        fprintf(stdout, "Detected %u-byte tape block size.\n",
                                tapeBlockSize);
                }
            }
            else
            {
                result = read(mtfd, tBuffer, remaining, tapeBlockSize);

                if (result < 0)
                {
                    fprintf(stderr, "Error reading tape!\n");
                    return(-1);
                }
                else if (result == 0)
                {
                    if (verbose > 0) fprintf(stdout, "Read filemark.\n");

                    filemark = true;
                    remaining = 0;

                    return(1);
                }

                remaining = result;

                while (remaining < flbSize)
                {
                    result = read(mtfd, tBuffer, remaining, tapeBlockSize);

                    if (result < 0)
                    {
                        fprintf(stderr, "Error reading tape!\n");
                        return(-1);
                    }
                    else if (result == 0)
                    {
                        /*
                        * tapeBlockSize is less than flbSize and an EOF was
                        * found after the first tape block. ALK 2000-07-17
                        */

                        if (verbose > 0) fprintf(stdout, "Read filemark.\n");

                        filemark = true;
                        remaining = 0;

                        return(1);
                    }


                    remaining += result;
                }
            }

            if (debug > 0)
            {
                fprintf(stderr, "remaining=%u\n", remaining);
                dump("lastblock.dmp", tBuffer, remaining);
            }

            blockCnt += 1;
        }
        else if (advance > remaining)
        {
            while (advance > remaining)
            {
                if (debug > 0) fprintf(stderr, "reading %u bytes...\n", tapeBlockSize);

                result = read(mtfd, tBuffer, remaining, tapeBlockSize);

                if (result < 0)
                {
                    fprintf(stderr, "Error reading tape!\n");
                    return(-1);
                }
                else if (result == 0)
                {
                    if (verbose > 0) fprintf(stdout, "Read filemark.\n");

                    filemark = true;
                    remaining = 0;

                    return(1);
                }

                remaining += result;
            }

            if (debug > 0) fprintf(stderr, "remaining=%u\n", remaining);
        }
        else
        {
            if (advance % flbSize != 0)
            {
                fprintf(stderr, "Illegal read request (%u bytes)!\n", advance);
                return(-1);
            }

            if (debug > 0) fprintf(stderr, "advancing %u bytes...\n", advance);

            remaining -= advance;
            memmove(tBuffer, tBuffer, advance, remaining);
        }

        return(0);
    }


    /* skipToNextBlock() skips over all streams until it finds a SPAD stream and  */
    /* then skips over it. The next descriptor block will immediately follow the  */
    /* SPAD stream. The tape read buffer will be advanced to this position. This  */
    /* function assumes the global variable 'stream' has been set to point to a   */
    /* stream header.                                                             */

    public override Int32 skipToNextBlock()
    {
        Int32 offset;

        while ((stream.id != MTF_STAN) && (stream.id != MTF_SPAD))
        {
            offset = skipOverStream();
            if (offset < 0)
            {
                fprintf(stderr, "Error traversing stream!\n");
                return(-1);
            }

            stream = access<MTF_STREAM_HDR>(tBuffer, offset);
        }

        if (stream.id == MTF_STAN)
        {
            offset = skipOverStream();
            if (offset < 0)
            {
                fprintf(stderr, "Error traversing stream!\n");
                return(-1);
            }

            stream = access<MTF_STREAM_HDR>(tBuffer, offset);

            if ((offset % flbSize) != 0)
            {
                while (stream.id != MTF_SPAD)
                {
                    offset = skipOverStream();
                    if (offset < 0)
                    {
                        fprintf(stderr, "Error traversing stream!\n");
                        return(-1);
                    }

                    stream = access<MTF_STREAM_HDR>(tBuffer, offset);
                }

                offset = skipOverStream();
                if (offset < 0)
                {
                    fprintf(stderr, "Error traversing stream!\n");
                    return(-1);
                }
            }
        }
        else
        {
            offset = skipOverStream();
            if (offset < 0)
            {
                fprintf(stderr, "Error traversing stream!\n");
                return(-1);
            }
        }

        if (offset != 0)
        {
            fprintf(stderr, "Error skipping stream!\n");
            return(-1);
        }

        return(0);
    }


    /* skipOverStream() skips over the current stream. It returns the number of   *//* bytes to skip in the last tape block read.                                 */

    public override Int32 skipOverStream()
    {
        Int32 result;
        Int32 offset, bytes;
        MTF_STREAM_HDR hdr = new MTF_STREAM_HDR();

        offset = offsetOf(stream) - offsetOf(tBuffer);
        offset += Unsafe.SizeOf<MTF_STREAM_HDR>();

        if (debug > 0) fprintf(stderr, "offset=%lu remaining=%u\n", offset, remaining);

        if (offset >= remaining)
        {
            bytes = remaining - (offset - Unsafe.SizeOf<MTF_STREAM_HDR>());

            memcpy(ref hdr, addrOf(stream), bytes);

            offset -= remaining;

            result = readNextBlock(remaining);
            if (result != 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }

            if (offset > 0)
            {
                memcopy(ref hdr, bytes, tBuffer, offset);
            }
        }
        else
        {
            memcpy(ref hdr, addrOf(stream), Unsafe.SizeOf<MTF_STREAM_HDR>());
        }

        if (verbose > 1)
        {
            var ptr = getbytes(hdr.id);
            if ((isalnum(ptr[0])) && (isalnum(ptr[1])) &&
                (isalnum(ptr[2])) && (isalnum(ptr[3])))
            {
                fprintf(stdout, "Skipping %c%c%c%c stream...\n",
                        ptr[0], ptr[1], ptr[2], ptr[3]);
            }
            else
            {
                fprintf(stderr, "Error seeking next stream!\n");
                return(-1);
            }

            fprintf(stdout, "System Attributes: %04X\n", hdr.sysAttr);
            fprintf(stdout, "Media Attributes: %04X\n", hdr.mediaAttr);
            fprintf(stdout, "Stream Length: %luu\n", hdr.length);
            fprintf(stdout, "Data Encryption: %u\n", hdr.encrypt);
            fprintf(stdout, "Data Compression: %u\n", hdr.compress);
        }

        if (debug > 0) fprintf(stderr, "remaining=%u\n", remaining);

        bytes = remaining - offset;

        if (debug > 0)
            fprintf(stderr, "skipping %lu bytes from offset %lu...\n", bytes, offset);

        hdr.length = hdr.length - (UInt64)bytes;

        if (debug > 0)
            fprintf(stderr, "%luu not yet skipped\n", hdr.length);

        if (hdr.length == 0)
        {
            offset += bytes;
        }
        else
        {
            while (hdr.length > 0)
            {
                result = readNextBlock(0);
                if (result != 0)
                {
                    fprintf(stderr, "Error reading tape block!\n");
                    return(-1);
                }

                bytes = remaining;

                if (debug > 0)
                    fprintf(stderr, "skipping %lu bytes from offset 0...\n", bytes);

                hdr.length = hdr.length - (UInt64)bytes;

                if (debug > 0)
                    fprintf(stderr, "%luu not yet skipped\n", hdr.length);
            }

            offset = bytes;
        }

        if ((offset % 4) != 0)
            offset += 4 - (offset % 4);

        if (offset >= flbSize)
        {
            bytes = offset;
            bytes -= bytes % flbSize;
            offset -= bytes;

            result = readNextBlock(bytes);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        if (debug > 0) fprintf(stderr, "returning %ld\n", offset);

        return(offset);
    }


    /* writeData() reads the contents of a the current stream (which should be a  */
    /* STAN stream) and writes it to a file. It returns the number of bytes that  */
    /* were written from the last tape block read.                                */

    public override Int32 writeData(FileStream file)
    {
        Int32 result;
        Int32 offset, bytes;
        MTF_STREAM_HDR hdr = new MTF_STREAM_HDR();

        offset = offsetOf(stream) - offsetOf(tBuffer);
        offset += Unsafe.SizeOf<MTF_STREAM_HDR>();

        if (debug > 0) fprintf(stderr, "offset=%lu remaining=%u\n", offset, remaining);

        if (offset >= remaining)
        {
            bytes = remaining - (offset - Unsafe.SizeOf<MTF_STREAM_HDR>());

            memcpy(ref hdr, addrOf(stream), bytes);

            offset -= remaining;

            result = readNextBlock(0);
            if (result != 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }

            if (offset > 0)
            {
                memcopy(ref hdr, bytes, tBuffer, offset);
            }
        }
        else
        {
            memcpy(ref hdr, addrOf(stream), Unsafe.SizeOf<MTF_STREAM_HDR>());
        }

        if (verbose > 1)
        {
            fprintf(stdout, "System Attributes: %04X\n", hdr.sysAttr);
            fprintf(stdout, "Media Attributes: %04X\n", hdr.mediaAttr);
            fprintf(stdout, "Stream Length: %luu\n", hdr.length);
            fprintf(stdout, "Data Encryption: %u\n", hdr.encrypt);
            fprintf(stdout, "Data Compression: %u\n", hdr.compress);
        }

        if ((compressPossible) && ((hdr.mediaAttr & MTF_STREAM_COMPRESSED) != 0))
        {
            fprintf(stderr, "Compressed streams are not supported!\n");
            return(-1);
        }

        if ((hdr.sysAttr & MTF_STREAM_IS_SPARSE) != 0)
        {
            fprintf(stderr, "Sparse streams are not supported!\n");
            return(-1);
        }

        if (debug > 0) fprintf(stderr, "remaining=%u\n", remaining);

        bytes = remaining - offset;

        if (debug > 0)
            fprintf(stderr, "writing %lu bytes from offset %lu...\n", bytes, offset);

        if (write(file, tBuffer, offset, bytes) != bytes)
        {
            fprintf(stderr, "Error writing file!\n");
            return(-1);
        }

        hdr.length = hdr.length - (UInt64)bytes;

        if (debug > 0)
            fprintf(stderr, "%luu not yet written\n", hdr.length);

        if (hdr.length == 0)
        {
            offset += bytes;
        }
        else
        {
            while (hdr.length > 0)
            {
                result = readNextBlock(0);
                if (result != 0)
                {
                    fprintf(stderr, "Error reading tape block!\n");
                    return(-1);
                }

                bytes = remaining;

                if (debug > 0)
                    fprintf(stderr, "writing %lu bytes from offset 0...\n", bytes);

                if (write(file, tBuffer, 0, bytes) != bytes)
                {
                    fprintf(stderr, "Error writing file!\n");
                    return(-1);
                }

                hdr.length = hdr.length - (UInt64)bytes;

                if (debug > 0)
                    fprintf(stderr, "%luu not yet written\n", hdr.length);
            }

            offset = bytes;
        }

        if ((offset % 4) != 0)
            offset += 4 - (offset % 4);

        if (offset >= flbSize)
        {
            bytes = offset;
            bytes -= bytes % flbSize;
            offset -= bytes;

            result = readNextBlock(bytes);
            if (result < 0)
            {
                fprintf(stderr, "Error reading tape block!\n");
                return(-1);
            }
        }

        if (debug > 0) fprintf(stderr, "returning %ld\n", offset);

        return(offset);
    }


    /* getString() fetches a string stored after a descriptor block. If the       */
    /* string is type 2, it is converted from unicode to ascii. Also, any nulls   */
    /* are replaced with '/' characters. A pointer to the string is returned. If  */
    /* the string is longer than the maximum supported, an empty string is        */
    /* returned.                                                                  */

    public override string getString<T>(Byte type, UInt16 length, T addr, UInt16 offset)
    {
        var ptr = addrOf(addr);

        if (length == 0)
        {
            return("");
        }
        else
        {
            var buffer = ptr.Slice(offset, length);
            string str;

            if (type == 2)
            {
                str = System.Text.Encoding.Unicode.GetString(buffer.Span);
            }
            else
            {
                str = System.Text.Encoding.ASCII.GetString(buffer.Span);
            }

            str = str.Replace((char)0, '/');

            return(str);
        }
    }

}
