using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FirmwareGen.GPT
{
    internal class GPTUtils
    {
        internal static byte[] MakeGPT(ulong DiskSize, ulong SectorSize, GPTPartition[] DefaultPartitionTable, Guid DiskGuid, bool IsBackupGPT = false, bool SplitInHalf = true, ulong AndroidDesiredSpace = 4_294_967_296)
        {
            ulong FirstLBA = 1;
            ulong LastLBA = (DiskSize / SectorSize) - 1;

            ulong PartitionArrayLBACount = 4;

            if ((ulong)DefaultPartitionTable.Length * 128 > PartitionArrayLBACount * SectorSize)
            {
                throw new Exception("Unsupported Configuration, too many partitions to fit. File an issue");
            }

            ulong TotalGPTLBACount = 1 /* GPT Header */ + PartitionArrayLBACount /* Partition Table */;
            ulong LastUsableLBA = LastLBA - TotalGPTLBACount;

            List<GPTPartition> Partitions = new(DefaultPartitionTable);
            Partitions[^1].LastLBA = LastUsableLBA;

            if (AndroidDesiredSpace < 4_294_967_296)
            {
                throw new Exception("ERROR");
            }

            InjectWindowsPartitions(Partitions, SectorSize, 4, SplitInHalf, AndroidDesiredSpace);

            return MakeGPT(FirstLBA, LastLBA, SectorSize, Partitions.ToArray(), DiskGuid, PartitionArrayLBACount: PartitionArrayLBACount, IsBackupGPT: IsBackupGPT);
        }

        private static void InjectWindowsPartitions(List<GPTPartition> Partitions, ulong SectorSize, ulong BlockSize, bool SplitInHalf, ulong AndroidDesiredSpace = 4_294_967_296)
        {
            ulong FirstUsableLBA = Partitions.Last().FirstLBA;
            ulong LastUsableLBA = Partitions.Last().LastLBA;

            if (LastUsableLBA % BlockSize != 0)
            {
                LastUsableLBA -= LastUsableLBA % BlockSize;
            }

            ulong UsableLBACount = LastUsableLBA - FirstUsableLBA + 1;

            ulong SixtyFourGigaBytes = 68_719_476_736 / SectorSize;

            ulong ESPLBACount = 65525 + 1024 + 1 /* Cluster Size Limit for FAT32 */;
            if (ESPLBACount % BlockSize != 0)
            {
                ESPLBACount += BlockSize - (ESPLBACount % BlockSize);
            }

            ulong WindowsLBACount;

            /* Strategy to reserve half for Android, half for Windows */
            if (SplitInHalf)
            {
                ulong AndroidOtherLUNLBAUsage = 8_679_372 /* Size taken in Android by another LUN that counts towards Android space utilization */;
                WindowsLBACount = (UsableLBACount + AndroidOtherLUNLBAUsage - ESPLBACount) / 2;

                if (WindowsLBACount < SixtyFourGigaBytes)
                {
                    WindowsLBACount = SixtyFourGigaBytes;
                }

                // In the case of the 4GB for Android strategy, we cannot do this or we risk to get userdata < 4GB
                if (WindowsLBACount % BlockSize != 0)
                {
                    WindowsLBACount += BlockSize - (WindowsLBACount % BlockSize);
                }
            }
            /* Strategy to reserve 4GB for Android Only */
            else
            {
                ulong FourGigaBytes = AndroidDesiredSpace / SectorSize;
                WindowsLBACount = UsableLBACount - ESPLBACount - FourGigaBytes;

                if (WindowsLBACount < SixtyFourGigaBytes)
                {
                    WindowsLBACount = SixtyFourGigaBytes;
                }

                if (WindowsLBACount % BlockSize != 0)
                {
                    WindowsLBACount -= WindowsLBACount % BlockSize;
                }
            }

            ulong TotalInjectedLBACount = ESPLBACount + WindowsLBACount;

            ulong ESPFirstLBA = LastUsableLBA - TotalInjectedLBACount;
            ulong ESPLastLBA = ESPFirstLBA + ESPLBACount - 1;

            ulong WindowsFirstLBA = ESPLastLBA + 1;
            ulong WindowsLastLBA = ESPLastLBA + WindowsLBACount;

            if (ESPFirstLBA % BlockSize != 0)
            {
                ulong Padding = BlockSize - (ESPFirstLBA % BlockSize);
                throw new Exception("ESPFirstLBA overflew block alignment by: " + Padding);
            }

            if ((ESPLastLBA + 1) % BlockSize != 0)
            {
                ulong Padding = BlockSize - ((ESPLastLBA + 1) % BlockSize);
                throw new Exception("ESPLastLBA + 1 overflew block alignment by: " + Padding);
            }

            if (WindowsFirstLBA % BlockSize != 0)
            {
                ulong Padding = BlockSize - (WindowsFirstLBA % BlockSize);
                throw new Exception("WindowsFirstLBA overflew block alignment by: " + Padding);
            }

            if ((WindowsLastLBA + 1) % BlockSize != 0)
            {
                ulong Padding = BlockSize - ((WindowsLastLBA + 1) % BlockSize);
                throw new Exception("WindowsLastLBA + 1 overflew block alignment by: " + Padding);
            }

            Partitions.Add(new()
            {
                TypeGUID = new Guid("c12a7328-f81f-11d2-ba4b-00a0c93ec93b"),
                UID = new Guid("dec2832a-5f6c-430a-bd85-42551bce7b91"),
                FirstLBA = ESPFirstLBA,
                LastLBA = ESPLastLBA,
                Attributes = 0,
                Name = "esp"
            });

            Partitions.Add(new()
            {
                TypeGUID = new Guid("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7"),
                UID = new Guid("92dee62d-ed67-4ec3-9daa-c9a4bce2c355"),
                FirstLBA = WindowsFirstLBA,
                LastLBA = WindowsLastLBA,
                Attributes = 0,
                Name = "win"
            });

            Partitions[^3].LastLBA = ESPFirstLBA - 1;

            ConsoleColor ogColor = Console.ForegroundColor;

            ulong androidSpaceInBytes = (Partitions[^3].LastLBA - Partitions[^3].FirstLBA) * SectorSize;
            ulong windowsSpaceInBytes = (Partitions[^1].LastLBA - Partitions[^1].FirstLBA) * SectorSize;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Android usable space (in bytes): " + androidSpaceInBytes);
            Console.WriteLine("Windows usable space (in bytes): " + windowsSpaceInBytes);
            Console.ForegroundColor = ogColor;
        }

        internal static byte[] MakeGPT(ulong FirstLBA, ulong LastLBA, ulong SectorSize, GPTPartition[] PartitionTable, Guid DiskGuid, ulong PartitionArrayLBACount = 4, bool IsBackupGPT = false)
        {
            byte[] Result = new byte[512 * (PartitionArrayLBACount + 1)];

            GPTHeader Header = new()
            {
                Signature = "EFI PART",
                Revision = 0x10000,
                HeaderSize = 92,
                CRC32 = 0,
                Reserved = 0,
                MyLBA = FirstLBA,
                AlternateLBA = IsBackupGPT ? LastLBA : 0,
                FirstUsableLBA = FirstLBA + 1 + PartitionArrayLBACount,
                LastUsableLBA = LastLBA - PartitionArrayLBACount,
                DiskGUID = DiskGuid,
                PartitionArrayLBA = LastLBA - PartitionArrayLBACount + 1,
                PartitionArrayLBACount = PartitionArrayLBACount,
                PartitionArraySize = PartitionArrayLBACount * SectorSize
            };

            byte[] PartitionTableBytes = new byte[PartitionArrayLBACount * SectorSize];
            for (int i = 0; i < PartitionTable.Length; i++)
            {
                byte[] PartitionBytes = PartitionTable[i].ToBytes();
                PartitionBytes.CopyTo(PartitionTableBytes, i * 128);
            }

            Header.UpdateCRC32();

            byte[] HeaderBytes = Header.ToBytes();
            HeaderBytes.CopyTo(Result, 0);
            PartitionTableBytes.CopyTo(Result, 512);

            return Result;
        }
    }
}
