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

See mtf.c for version history, contibutors, etc.


**
**	mtf.h
**
**	defines, types for the Microsoft Tape Format
**	prototypes for functions in mtfread.c and mtfutil.c
**
*/


using System;
using System.Runtime.InteropServices;

public abstract class imtf {

    //#pragma pack(1)

    public const int MIN_TAPE_BLOCK_SIZE = 512;
    public const int MAX_TAPE_BLOCK_SIZE = 65536;
    public const int MAX_PRINT_STRING = 100;
    public const int MAX_PATTERN_LEN = 100;
    public const int MAX_PATTERN = 20;

    public const int CASE_SENSITIVE = 0;
    public const int CASE_LOWER = 1;
    public const int CASE_UPPER = 2;

    /* pointer to non-fixed length information */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_TAPE_ADDRESS
    {
        public UInt16	size;	/* size of referenced field */
        public UInt16	offset; /* offset to start of field from start of structure */
    };

    /* storage of date and time */
    //typedef Byte MTF_DATE_TIME[5];	/* 14 bit year, 4 bit month, 5 bit day, */
                                        /* 5 bit hour, 6 bit minute, 6 bit second */
    [StructLayout(LayoutKind.Explicit, Size=5, Pack=0)]
    public struct MTF_DATE_TIME {

        [FieldOffset(0)]
        public Byte[] array;
    }

    /* macros for reading the MTF_DATE_TIME type */
    public UInt16 MTF_YEAR(MTF_DATE_TIME X) => (UInt16) ((X.array[0] << 6) | (X.array[1] >> 2));
    public Byte MTF_MONTH(MTF_DATE_TIME X) => (Byte) (((X.array[1] & 0x03) << 2) | ((X.array[2] & 0xC0) >> 6));
    public Byte MTF_DAY(MTF_DATE_TIME X) => (Byte) ((X.array[2] & 0x3E) >> 1);
    public Byte MTF_HOUR(MTF_DATE_TIME X) => (Byte) (((X.array[2] & 0x01) << 4) | ((X.array[3] & 0xF0) >> 4));
    public Byte MTF_MINUTE(MTF_DATE_TIME X) => (Byte) (((X.array[3] & 0x0F) << 2) | ((X.array[4] & 0xC0) >> 6));
    public Byte MTF_SECOND(MTF_DATE_TIME X) => (Byte) (X.array[4] & 0x3F);

    /* common descriptor block header */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_DB_HDR
    {
        [StructLayout(LayoutKind.Sequential, Size=6, Pack=0)]
        public struct rsv1Filler {
            public Byte b;
        }
        [StructLayout(LayoutKind.Sequential, Size=4, Pack=0)]
        public struct rsv2Filler {
            public Byte b;
        }


        public UInt32				type;		/* DBLK type */
        public UInt32				attr;		/* block attributes */
        public UInt16				off;		/* offset to first event */
        public Byte		    		osId;		/* OS ID */
        public Byte		    		osVer;		/* OS version */
        public UInt64				size;		/* displayable size */
        public UInt64				fla;		/* format logical address */
        public UInt16 				mbc;		/* reserved for MBC */
        public rsv1Filler			rsv1;	    /* reserved for future use */
        public UInt32				cbId;		/* control block ID */
        public rsv2Filler			rsv2;   	/* reserved for future use */
        public MTF_TAPE_ADDRESS	    osData;		/* OS-specific data */
        public Byte		    		strType;	/* string type */
        public Byte		    		rsv3;		/* reserved for future use */
        public UInt16				check;		/* header checksum */
    };

    /* values for MTF_DB_HDR.type field */
    public const int MTF_TAPE = 0x45504154;
    public const int MTF_SSET = 0x54455353;
    public const int MTF_VOLB = 0x424C4F56;
    public const int MTF_DIRB = 0x42524944;
    public const int MTF_FILE = 0x454C4946;
    public const int MTF_CFIL = 0x4C494643;
    public const int MTF_ESPB = 0x42505345;
    public const int MTF_ESET = 0x54455345;
    public const int MTF_EOTM = 0x4D544F45;
    public const int MTF_SFMB = 0x424D4653;

    /* bit masks for MTF_DB_HDR.attr field for all values of MTF_DB_HDR.type */
    public const int MTF_CONTINUATION = 0x0001;
    public const int MTF_COMPRESSION = 0x0002;
    public const int MTF_EOS_AT_EOM = 0x0004;

    /* bit masks for MTF_DB_HDR.attr field for MTF_DB_HDR.type = MTF_TAPE */
    public const int MTF_SET_MAP_EXISTS = 0x0100;
    public const int MTF_FDD_ALLOWED = 0x0200;

    /* bit masks for MTF_DB_HDR.attr field for MTF_DB_HDR.type = MTF_SSET */
    public const int MTF_FDD_EXISTS = 0x0100;
    public const int MTF_ENCRYPTION = 0x0200;

    /* bit masks for MTF_DB_HDR.attr field for MTF_DB_HDR.type = MTF_ESET */
    public const int MTF_FDD_ABORTED = 0x0100;
    public const int MTF_END_OF_FAMILY = 0x0200;
    public const int MTF_ABORTED_SET = 0x0400;

    /* bit masks for MTF_DB_HDR.attr field for MTF_DB_HDR.type = MTF_EOTM */
    public const int MTF_NO_ESET_PBA = 0x0100;
    public const int MTF_INVALID_ESET_PBA = 0x0200;

    /* values for MTF_DB_HDR.osId field */
    public const int MTF_OS_NETWARE = 1;
    public const int MTF_OS_NETWARE_SMS = 13;
    public const int MTF_OS_WINDOWS_NT = 14;
    public const int MTF_OS_DOS = 24;
    public const int MTF_OS_OS2 = 25;
    public const int MTF_OS_WINDOWS_95 = 26;
    public const int MTF_OS_MACINTOSH = 27;
    public const int MTF_OS_UNIX = 28;

    /* values for MTF_DB_HDR.strType field */
    public const int MTF_NO_STRINGS = 0;
    public const int MTF_ANSI_STR = 1;
    public const int MTF_UNICODE_STR = 2;

    /* structure pointed to by the MTF_DB_HDR.osData field when MTF_DB_HDR.osId = */
    /* MTF_OS_WINDOWS_NT and MTF_DB_HDR.osVer = 0 */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_OS_DATA_WINDOWS_NT

    {
        public UInt32	attr;	/* file attributes */
        public UInt16	off;	/* short name offset */
        public UInt16	size;	/* short name size */
        public Boolean	link;	/* if non-zero the file is a link to a previous file */
        public UInt16	rsv;	/* reserved for future use */
    };

    /* descriptor block for MTF_DB_HDR.type = MTF_TAPE (tape header) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_TAPE_BLK

    {
        public MTF_DB_HDR			common;		/* common block header */
        public UInt32				famId;		/* media family ID */
        public UInt32				attr;		/* TAPE attributes */
        public UInt16				seq;		/* media sequence number */
        public UInt16				encrypt;	/* password encryption */
        public UInt16				sfmSize;	/* soft filemark block size */
        public UInt16				catType;	/* media-based catalog type */
        public MTF_TAPE_ADDRESS	    name;		/* media name */
        public MTF_TAPE_ADDRESS	    desc;		/* media desc./label */
        public MTF_TAPE_ADDRESS	    passwd;		/* media password */
        public MTF_TAPE_ADDRESS	    software;	/* software name */
        public UInt16				flbSize;	/* format logical block size */
        public UInt16				vendorId;	/* software vendor ID */
        public MTF_DATE_TIME		date;		/* media date */
        public Byte				    ver;		/* MTF major version */
    };

    /* bitmasks for MTF_TAPE_BLK.attr */
    public const int MTF_TAPE_SOFT_FILEMARK_BIT = 0x00000001;
    public const int MTF_TAPE_MEDIA_LABEL_BIT = 0x00000002;

    /* values for MTF_TAPE_BLK.catType */
    public const int MTF_NO_MBC = 0;
    public const int MTF_MBC_TYPE_1 = 1;
    public const int MTF_MBC_TYPE_2 = 2;

    /* descriptor block for MTF_DB_HDR.type = MTF_SSET (start of data set) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_SSET_BLK

    {
        public MTF_DB_HDR			common;			/* common block header */
        public UInt32				attr;			/* SSET attributes */
        public UInt16				passEncrypt;	/* password encryption */
        public UInt16				softCompress;	/* software compression */
        public UInt16				vendor;			/* software vendor ID */
        public UInt16				num;			/* data set number */
        public MTF_TAPE_ADDRESS 	name;			/* data set name */
        public MTF_TAPE_ADDRESS 	desc;			/* data set description */
        public MTF_TAPE_ADDRESS 	passwd;			/* data set password */
        public MTF_TAPE_ADDRESS 	user;			/* user name */
        public UInt64				pba;			/* physical block address */
        public MTF_DATE_TIME		date;			/* media write date */
        public Byte				    major;			/* software major version */
        public Byte	    			minor;			/* software minor version */
        public SByte				tz;				/* time zone */
        public Byte		    		ver;			/* MTF minor version */
        public Byte			    	catVer;			/* media catalog version 8/ */
    };

    /* bitmasks for MTF_SSET_BLK.attr */
    public const int MTF_SSET_TRANSFER_BIT = 0x00000001;
    public const int MTF_SSET_COPY_BIT = 0x00000002;
    public const int MTF_SSET_NORMAL_BIT = 0x00000004;
    public const int MTF_SSET_DIFFERENTIAL_BIT = 0x00000008;
    public const int MTF_SSET_INCREMENTAL_BIT = 0x00000010;
    public const int MTF_SSET_DAILY_BIT = 0x00000020;

    /* value for MTF_SSET_BLK.tz when local time is not coordinated with UTC */
    public const int MTF_LOCAL_TZ = 127;

    /* descriptor block for MTF_DB_HDR.type = MTF_VOLB (volume) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_VOLB_BLK

    {
        public MTF_DB_HDR			common;		/* common block header */
        public UInt32				attr;		/* VOLB attributes */
        public MTF_TAPE_ADDRESS 	device;		/* device name */
        public MTF_TAPE_ADDRESS 	volume;		/* volume name */
        public MTF_TAPE_ADDRESS 	machine;	/* machine name */
        public MTF_DATE_TIME		date;		/* media write date */
    };

    /* bitmasks for MTF_VOLB_BLK.attr */
    public const int MFT_VOLB_NO_REDIRECT_RESTORE_BIT = 0x00000001;
    public const int MFT_VOLB_NON_VOLUME_BIT = 0x00000002;
    public const int MFT_VOLB_DEV_DRIVE_BIT = 0x00000004;
    public const int MFT_VOLB_DEV_UNC_BIT = 0x00000008;
    public const int MFT_VOLB_DEV_OS_SPEC_BIT = 0x00000010;
    public const int MFT_VOLB_DEV_VEND_SPEC_BIT = 0x00000020;

    /* descriptor block for MTF_DB_HDR.type = MTF_DIRB (directory) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_DIRB_BLK

    {
        public MTF_DB_HDR			common;		/* common block header */
        public UInt32				attr;		/* DIRB attributes */
        public MTF_DATE_TIME		mod;		/* last modification date */
        public MTF_DATE_TIME		create;		/* creation date */
        public MTF_DATE_TIME		backup;		/* backup date */
        public MTF_DATE_TIME		access;		/* last access date */
        public UInt32				id;			/* directory ID */
        public MTF_TAPE_ADDRESS	    name;		/* directory name */
    };

    /* bitmasks for MTF_DIRB_BLK.attr */
    public const int MTF_DIRB_READ_ONLY_BIT = 0x00000100;
    public const int MTF_DIRB_HIDDEN_BIT = 0x00000200;
    public const int MTF_DIRB_SYSTEM_BIT = 0x00000400;
    public const int MTF_DIRB_MODIFIED_BIT = 0x00000800;
    public const int MTF_DIRB_EMPTY_BIT = 0x00010000;
    public const int MTF_DIR_PATH_IN_STREAM_BIT = 0x00020000;
    public const int MTF_DIRB_CORRUPT_BIT = 0x00040000;

    /* descriptor block for MTF_DB_HDR.type = MTF_FILE (file) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_FILE_BLK

    {
        public MTF_DB_HDR			common;		/* common block header */
        public UInt32				attr;		/* FILE attributes */
        public MTF_DATE_TIME		mod;		/* last modification date */
        public MTF_DATE_TIME		create;		/* creation date */
        public MTF_DATE_TIME		backup;		/* backup date */
        public MTF_DATE_TIME		access;		/* last access date */
        public UInt32				dirId;		/* directory ID */
        public UInt32				id;			/* file ID */
        public MTF_TAPE_ADDRESS 	name;		/* file name */
    };

    /* bitmasks for MTF_FILE_BLK.attr */
    public const int MTF_FILE_READ_ONLY_BIT = 0x00000100;
    public const int MTF_FILE_HIDDEN_BIT = 0x00000200;
    public const int MTF_FILE_SYSTEM_BIT = 0x00000400;
    public const int MTF_FILE_MODIFIED_BIT = 0x00000800;
    public const int MTF_FILE_IN_USE_BIT = 0x00010000;
    public const int MTF_FILE_NAME_IN_STREAM_BIT = 0x00020000;
    public const int MTF_FILE_CORRUPT_BIT = 0x00040000;

    /* descriptor block for MTF_DB_HDR.type = MTF_CFIL (corrupt object) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_CFIL_BLK

    {
        [StructLayout(LayoutKind.Sequential, Size=8, Pack=0)]
        public struct rsvFiller {
            public Byte b;
        }

        public MTF_DB_HDR		common;		/* common block header */
        public UInt32			attr;		/* CFIL attributes */
        public rsvFiller 		rsv;		/* reserved for future use */
        public UInt64			off;		/* stream offset */
        public UInt64			num;		/* corrupt stream number */
    };

    /* bitmasks for MTF_CFIL_BLK.attr */
    public const int MTF_CFIL_LENGTH_CHANGE_BIT = 0x00010000;
    public const int MTF_CFIL_UNREADABLE_BLK_BIT = 0x00020000;
    public const int MTF_CFIL_DEADLOCK_BIT = 0x00080000;
    public const int MTF_CFIL_READ_ONLY_BIT = 0x00000100;

    /* descriptor block for MTF_DB_HDR.type = MTF_ESPB (end of set pad) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_ESPB_BLK {
         public MTF_DB_HDR		common;		/* common block header */
    };

    /* descriptor block for MTF_DB_HDR.type = MTF_ESET (end of data set) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_ESET_BLK

    {
        public MTF_DB_HDR		common;		/* common block header */
        public UInt32			attr;		/* ESET attributes */
        public UInt32			corrupt;	/* number of corrupt files */
        public UInt64			mbc1;		/* reserved for MBC */
        public UInt64			mbc2;		/* reserved for MBC */
        public UInt16			seq;		/* FDD media sequence number */
        public UInt16			set;		/* data set number */
        public MTF_DATE_TIME	date;		/* media write date */
    };

    /* bitmasks for MTF_ESET_BLK.attr */
    public const int MTF_ESET_TRANSFER_BIT = 0x00000001;
    public const int MTF_ESET_COPY_BIT = 0x00000002;
    public const int MTF_ESET_NORMAL_BIT = 0x00000004;
    public const int MTF_ESET_DIFFERENTIAL_BIT = 0x00000008;
    public const int MTF_ESET_INCREMENTAL_BIT = 0x00000010;
    public const int MTF_ESET_DAILY_BIT = 0x00000020;

    /* descriptor block for MTF_DB_HDR.type = MTF_EOTM (end of tape) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_EOTM_BLK

    {
        public MTF_DB_HDR		common;		/* common block header */
        public UInt64			lastEset;	/* last ESET PBA */
    };

    /* descriptor block for MTF_DB_HDR.type = MTF_SFMB (soft filemark) */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_SFMB_BLK

    {
        public MTF_DB_HDR	common;		/* common block header */
        public UInt32		marks;		/* number of filemark entries */
        public UInt32		used;		/* filemark entries used */
    };

    /* stream header */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_STREAM_HDR

    {
        public UInt32	id;			/* stream ID */
        public UInt16	sysAttr;	/* stream file system attributes */
        public UInt16	mediaAttr;	/* stream media format attributes */
        public UInt64	length;		/* stream length */
        public UInt16	encrypt;	/* data encryption algorithm */
        public UInt16	compress;	/* data compression algorithm */
        public UInt16	check;		/* checksum */
    };

    /* bitmasks for MTF_STREAM_HDR.sysAttr */
    public const int MTF_STREAM_MODIFIED_FOR_READ = 0x00000001;
    public const int MTF_STREAM_CONTAINS_SECURITY = 0x00000002;
    public const int MTF_STREAM_IS_NON_PORTABLE = 0x00000004;
    public const int MTF_STREAM_IS_SPARSE = 0x00000008;

    /* bitmasks for MTF_STREAM_HDR.mediaAttr */
    public const int MTF_STREAM_CONTINUE = 0x00000001;
    public const int MTF_STREAM_VARIABLE = 0x00000002;
    public const int MTF_STREAM_VAR_END = 0x00000004;
    public const int MTF_STREAM_ENCRYPTED = 0x00000008;
    public const int MTF_STREAM_COMPRESSED = 0x00000010;
    public const int MTF_STREAM_CHECKSUMED = 0x00000020;
    public const int MTF_STREAM_EMBEDDED_LENGTH = 0x00000040;

    /* platform-independant stream data types */
    public const int MTF_STAN = 0x4E415453 /* standard */;
    public const int MTF_PNAM = 0x4D414E50 /* path */;
    public const int MTF_FNAM = 0x4D414E46 /* file name */;
    public const int MTF_CSUM = 0x4D555343 /* checksum */;
    public const int MTF_CRPT = 0x54505243 /* corrupt */;
    public const int MTF_SPAD = 0x44415053 /* pad */;
    public const int MTF_SPAR = 0x52415053 /* sparse */;
    public const int MTF_TSMP = 0x504D5354 /* set map, media based catalog, type 1 */;
    public const int MTF_TFDD = 0x44444654 /* fdd, media based catalog, type 1 */;
    public const int MTF_MAP2 = 0x3250414D /* set map, media based catalog, type 2 */;
    public const int MTF_FDD2 = 0x32444446 /* fdd, media based catalog, type 2 */;

    /* Windows NT stream data types */
    public const int MTF_ADAT = 0x54414441;
    public const int MTF_NTEA = 0x4145544E;
    public const int MTF_NACL = 0x4C43414E;
    public const int MTF_NTED = 0x4445544E;
    public const int MTF_NTQU = 0x5551544E;
    public const int MTF_NTPR = 0x5250544E;
    public const int MTF_NTOI = 0x494F544E;

    /* Windows 95 stream data types */
    public const int MTF_GERC = 0x43524547;

    /* Netware stream data types */
    public const int MTF_N386 = 0x3638334E;
    public const int MTF_NBND = 0x444E424E;
    public const int MTF_SMSD = 0x44534D53;

    /* OS/2 stream data types */
    public const int MTF_OACL = 0x4C43414F;

    /* Macintosh stream data types */
    public const int MTF_MRSC = 0x4353524D;
    public const int MTF_MPRV = 0x5652504D;
    public const int MTF_MINF = 0x464E494D;

    /* stream compression frame header */
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct MTF_CMP_HDR

    {
        public UInt16		id;				/* compression header id  - see define below */
        public UInt16		attr;			/* stream media format attributes */
        public UInt64		remain;			/* remaining stream size */
        public UInt32		uncompress;		/* uncompressed size */
        public UInt32		compress;		/* compressed size */
        public Byte		seq;			/* sequence number */
        public Byte		rsv;			/* reserved */
        public UInt16		check;			/* checksum */
    };

    public const int MTF_CMP_HDR_ID = 0x4846;


    /* prototypes for mtfread.c */
    public abstract Int32 openMedia();
    public abstract Int32 readDataSet();
    public abstract Int32 readEndOfDataSet();
    public abstract Int32 readTapeBlock();
    public abstract Int32 readStartOfSetBlock();
    public abstract Int32 readVolumeBlock();
    public abstract Int32 readDirectoryBlock();
    public abstract Int32 readFileBlock();
    public abstract Int32 readCorruptObjectBlock();
    public abstract Int32 readEndOfSetPadBlock();
    public abstract Int32 readEndOfSetBlock();
    public abstract Int32 readEndOfTapeMarkerBlock();
    public abstract Int32 readSoftFileMarkBlock();
    public abstract Int32 readNextBlock(Int32 advance);
    public abstract Int32 skipToNextBlock();
    public abstract Int32 skipOverStream();
    public abstract Int32 writeData(System.IO.FileStream file);
    public abstract string getString<T>(Byte type, UInt16 length, T addr, UInt16 offset);

}
