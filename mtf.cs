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


Contributors:

Andrew Barnett <mtf@precisionlinux.com>

    Andrew contributed some early patches to 0.1 for tape drives that use
    physical block sizes different from that used by my tape drive. The code
    itself has disappeared as I've reworked the source, but the ideas are still
    there.

Alex Krowitx <alexkrowitz@my-Deja.com>

    Alex provided a bug fix to 0.2 (which was never put in general release) for
    tapes having a physical block size smaller than the MTF logical block size.

Version history:

    0.1		DAS 3/22/1999 - initial release
    0.2.1	DAS 9/13/2000 - automatic determination of the tape drive's physical
    		block size; added -b switch to manually specify tape physical block
    		size; when using the pattern-matching features 0.5 creates only
    		those directories required for files that are actually read from
    		tape; renamed -f switch to -F (it seems best to reserve lower case
    		for frequently used switches); -F switch tests for free space at
    		file creation time rather than directory creation time; added
    		MTF_OPTS envinronment variable support


**
**    mtf.c
**
**    This is the main source code file for the progam mtf. It is a bare-bones
**    read for Microsoft Tape Format tapes. Many things unsupported!
**
*/

using System;
using System.IO;
using System.Text.RegularExpressions;
using static mtfutil;

class pmtf : mtfread {

    public string device;
    public int setNum;


    public int main(int argc, string[] argv)
    {
        Int32 result;
        mtop op;
        mtget get;
        Tuple<FileStream, IOException> inpt;

        verbose = 0;
        debug = 0;
        list = 0;
        setNum = 0;
        outPath = "";
        matchCnt = 0;
        tapeBlockSize = 0;
        minFree = 0;
        forceCase = CASE_SENSITIVE;

        if (parseArgs(argc, argv) != 0)
        {
            return(-1);
        }

        if (outPath.Length > 0 && outPath[0] != '/')
        {
            curPath = Directory.GetCurrentDirectory();
            sprintf(out outPath, "%s/%s", curPath, outPath);
            outPath = outPath.TrimEnd('/');
        }

        if (debug > 0)
        {
            verbose = 2;
            fprintf(stdout, "Debug mode selected.\n");
        }

        if (verbose == 1)
        {
            fprintf(stdout, "Verbose mode selected.\n");
        }
        else if (verbose > 1)
        {
            fprintf(stdout, "Very verbose mode selected.\n");

            if (list > 0) fprintf(stdout, "List mode selected.\n");

            if (forceCase == CASE_UPPER)
                fprintf(stdout, "Case forced to upper.\n");
            else if (forceCase == CASE_LOWER)
                fprintf(stdout, "Case forced to lower.\n");

            if (tapeBlockSize != (long) 0)
                fprintf(stdout, "Tape block size set to %u bytes.\n",
                        tapeBlockSize);

            if (setNum != 0) fprintf(stdout, "Set %u will be \n", setNum);

            fprintf(stdout, "Files will be written to %s.\n", outPath);
            fprintf(stdout, "Tape device will be %s.\n", device);

            if (minFree != 0)
                fprintf(stdout, "Free space of %lu bytes will be maintained.\n",
                        minFree);

            if (matchCnt > 0)
                fprintf(stdout, "%u patterns were found.\n", matchCnt);
        }

        if (!Directory.Exists(outPath) && !File.Exists(outPath))
        {
            fprintf(stderr, "Error testing for status of %s!\n", outPath);
            return(-1);
        }

        if (!Directory.Exists(outPath))
        {
            fprintf(stderr, "%s is not a directory!\n", outPath);
            return(-1);
        }

        curPath = "";

        inpt = open(device, O_RDONLY);
        if (inpt.Item2 != null)
        {
            fprintf(stderr, "Error opening %s for reading!\n", device);
            return(-1);
        }
        mtfd = inpt.Item1;

        if (openMedia() != 0)
        {
            fprintf(stderr, "Error opening tape!\n");
            goto error;
        }

        if (setNum > 1)
        {
            op.mt_op = MTFSF;
            op.mt_count = (setNum - 1) * 2;

            if (verbose > 0)
                fprintf(stdout, "Forwarding tape to data set #%u...\n", setNum);

            if (ioctl(mtfd, MTIOCTOP, ref op) != 0)
            {
                fprintf(stderr, "Error forwarding tape!\n");
                goto error;
            }

            if (debug > 0)
            {
                op.mt_op = MTNOP;
                op.mt_count = 0;

                if (ioctl(mtfd, MTIOCTOP, ref op) != 0)
                {
                    fprintf(stderr, "Error returned by MTIOCTOP!\n");
                    goto error;
                }

                if (ioctl(mtfd, MTIOCGET, out get) != 0)
                {
                    fprintf(stderr, "Error returned by MTIOCGET!\n");
                    goto error;
                }

                fprintf(stdout, "tape file no. %u\n", get.mt_fileno);
                fprintf(stdout, "tape block no. %u\n", get.mt_blkno);
            }

            if (readNextBlock(0) != 0)
            {
                fprintf(stderr, "Error reading first block of data set!\n");
                goto error;
            }
        }

    next:

        if (readDataSet() != 0)
        {
            fprintf(stderr, "Error reading data set!\n");
            goto error;
        }

        result = readEndOfDataSet();
        if (result < 0)
        {
            fprintf(stderr, "Error reading to end of data set!\n");
            goto error;
        }

        if ((result == 0) && (setNum == 0))
            goto next;

        if (verbose > 0) fprintf(stdout, "Successful read of archive!\n");

        close(mtfd);

        return(0);

    error:

        dump("errorblock.dmp", tBuffer, remaining);
        if (mtfd != null) close(mtfd);

        return(-1);
    }


    Int16 parseArgs(int argc, string[] argv)
    {
        int i;
        int p;
        char c;
        string str;

        i = 1;

        while ((i < argc) && (argv[i][0] == '-'))
        {
            p = 1;
            str = argv[i];
            c = argv[i][p];

            if (str.Length > 1)
            {
                while (c != '\0')
                {
                    if (c == 'v')
                    {
                        verbose = 1;
                    }
                    else if (c == 'V')
                    {
                        verbose = 2;
                    }
                    else if (c == 'D')
                    {
                        debug = 1;
                    }
                    else if (c == 'l')
                    {
                        list = 1;
                    }
                    else
                    {
                        fprintf(stderr, "Unrecognized switch (-%c)!\n", c);
                        usage();
                        return(-1);
                    }

                    p += 1;
                }
            }
            else
            {
                if (c == 'v')
                {
                    verbose = 1;
                }
                else if (c == 'V')
                {
                    verbose = 2;
                }
                else if (c == 'D')
                {
                    debug = 1;
                }
                else if (c == 'l')
                {
                    list = 1;
                }
                else if (str == "s")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -s switch!\n");
                        usage();
                        return(-1);
                    }

                    if (whichSet(argv[i]) != 0)
                        return(-1);
                }
                else if (str == "d")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -d switch!\n");
                        usage();
                        return(-1);
                    }

                    if (whichDevice(argv[i]) != 0)
                        return(-1);
                }
                else if (str == "b")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -b switch!\n");
                        usage();
                        return(-1);
                    }

                    if (setBlockSize(argv[i]) != 0)
                        return(-1);
                }
                else if (str == "o")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -o switch!\n");
                        usage();
                        return(-1);
                    }

                    if (setPath(argv[i]) != 0)
                        return(-1);
                }
                else if (str == "c")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -c switch!\n");
                        usage();
                        return(-1);
                    }

                    if (setCase(argv[i]) != 0)
                        return(-1);
                }
                else if (str == "F")
                {
                    i += 1;

                    if (i == argc)
                    {
                        fprintf(stderr, "Argument required for -F switch!\n");
                        usage();
                        return(-1);
                    }

                    if (setMinFree(argv[i]) != 0)
                        return(-1);
                }
                else
                {
                    fprintf(stderr, "Unrecognized switch (-%c)!\n", c);
                    usage();
                    return(-1);
                }
            }

            i += 1;
        }

        if (i < argc)
        {
            if (getPatterns(argc, argv, i) != 0)
                return(-1);
            else
                return(0);
        }

        return(0);
    }


    Int16 whichSet(string argv)
    {
        UInt16 test;

        if (argv == "*")
        {
            setNum = 0;
        }
        else
        {
            if (!UInt16.TryParse(argv, out test))
            {
                fprintf(stderr,
                        "Unable to parse value given for set number (-s)!\n");
                usage();
                return(-1);
            }

            if ((test < 1) || (test > 65535))
            {
                fprintf(stderr,
                        "Value given for set number (-s) is out of range!\n");
                usage();
                return(-1);
            }

            setNum = test;
        }

        return(0);
    }


    Int16 whichDevice(string argv)
    {
        device = argv;

        return(0);
    }


    Int16 setBlockSize(string argv)
    {
        UInt32 test;

        if (!UInt32.TryParse(argv, out test))
        {
            fprintf(stderr, "Unable to parse value given for block size (-b)!\n");
            usage();
            return(-1);
        }

        if ((test < (UInt32) MIN_TAPE_BLOCK_SIZE) ||
            (test > (UInt32) MAX_TAPE_BLOCK_SIZE))
        {
            fprintf(stderr,
                    "Value given for block size (-B) is out of range (%u-%u)!\n",
                    MIN_TAPE_BLOCK_SIZE, MAX_TAPE_BLOCK_SIZE);
            usage();
            return(-1);
        }

        tapeBlockSize = (ushort) test;

        return(0);
    }


    Int16 setPath(string argv)
    {
        outPath = argv;

        return(0);
    }


    Int16 setCase(string argv)
    {
        if (argv.Length == 0)
        {
            fprintf(stderr, "No value given for forcing case (-c)!\n");
            usage();
            return(-1);
        }

        argv = argv.ToLowerInvariant();

        if (argv == "lower")
            forceCase = CASE_LOWER;
        else if (argv == "upper")
            forceCase = CASE_UPPER;
        else
        {
            fprintf(stderr, "Invalid value given for forcing case (-c) - \"%s\"!\n", argv);
            usage();
            return(-1);
        }

        return(0);
    }


    Int16 setMinFree(string argv)
    {
        int ix;
        string str;
        UInt32 multiplier;

        if (argv.Length == 0)
        {
            fprintf(stderr, "No value given for minimum free space (-f)!\n");
            usage();
            return(-1);
        }

        argv = argv.ToLowerInvariant();

        ix = 0;
        while ((argv[ix] >= '0') && (argv[ix] <= '9'))
            ix += 1;
        str = argv.Substring(ix);
        argv = argv.Substring(0, ix);

        if (str.Length == 0)
            multiplier = 0;
        if (str == "k")
            multiplier = 1024;
        else if (str == "m")
            multiplier = 1048576;
        else
        {
            fprintf(stderr,
                    "Invalid multiplier given for minimum free space (-f)!\n");
            usage();
            return(-1);
        }

        if (!uint.TryParse(argv, out minFree))
        {
            fprintf(stderr,
                    "Unable to parse value given for minimum free space (-f)!\n");
            usage();
            return(-1);
        }

        minFree *= multiplier;

        return(0);
    }


    Int16 getPatterns(int argc, string[] argv, int start)
    {
        int i;

        i = start;
        while (i < argc)
        {
            if (argv[i][0] == '-')
            {
                fprintf(stderr, "Error parsing pattern!\n");
                usage();
                return(-1);
            }

            if (argv[i].Length >= MAX_PATTERN_LEN)
            {
                fprintf(stderr, "Pattern exceeds maximum length!\n");
                usage();
                return(-1);
            }

            if (matchCnt == MAX_PATTERN)
            {
                fprintf(stderr, "Maximum number of patterns exceeded!\n");
                usage();
                return(-1);
            }

            if (regcomp(ref match[matchCnt], argv[i],
                        RegexOptions.IgnoreCase | RegexOptions.Singleline) != 0)
            {
                fprintf(stderr, "Invalid pattern - \"%s\"!\n", argv[i]);
                usage();
                return(-1);
            }

            matchCnt += 1;

            i += 1;
        }

        return(0);
    }


    void usage()
    {
        fprintf(stderr, "Usage: mtf [options] [pattern(s)]\n");
        fprintf(stderr, "    -v               verbose\n");
        fprintf(stderr, "    -V               very verbose\n");
        fprintf(stderr, "    -D               debug\n");
        fprintf(stderr, "    -l               list contents\n");
        fprintf(stderr, "    -b bytes         tape block size\n");
        fprintf(stderr, "    -d device        device/file to read from\n");
        fprintf(stderr, "    -s set           number of data set to read\n");
        fprintf(stderr, "    -c [lower|upper] force the case of paths\n");
        fprintf(stderr, "    -o path          root path to write files to\n");
        fprintf(stderr, "    -F bytes[K|M]    maintain minimum free space of bytes;\n");
        fprintf(stderr, "                     a K or M suffix signifies kilobytes or megabytes\n");
        fprintf(stderr, "    pattern(s)       only read file paths that match regex pattern(s)\n");

        return;
    }

}
