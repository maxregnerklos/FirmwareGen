using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FirmwareGen.GPT
{
    internal class GPTUtils
    {
        internal static byte[] MakeGPT(ulong diskSize, ulong sectorSize, GPTPartition[] defaultPartitionTable, Guid diskGuid, bool isBackupGPT = false, bool splitInHalf = true, ulong androidDesiredSpace = 4_294_967_296)
        {
            ulong firstLBA = 1;
            ulong lastLBA = (diskSize / sectorSize) - 1;

            ulong partitionArrayLBACount = 4;

            if ((ulong)defaultPartitionTable.Length * 128 > partitionArrayLBACount * sectorSize)
            {
                throw new Exception("Unsupported Configuration, too many partitions to fit. File an issue.");
            }

            ulong totalGPTLBACount = 1 /* GPT Header */ + partitionArrayLBACount /* Partition Table */;
            ulong lastUsableLBA = lastLBA - totalGPTLBACount;

            List<GPTPartition> partitions = new List<GPTPartition>(defaultPartitionTable);
            partitions[^1].LastLBA = lastUsableLBA;

            if (androidDesiredSpace < 4_294_967_296)
            {
                throw new Exception("ERROR: Android desired space cannot be less than 4GB.");
            }

            InjectWindowsPartitions(partitions, sectorSize, 4, splitInHalf, androidDesiredSpace);

            return MakeGPT(firstLBA, lastLBA, sectorSize, partitions.ToArray(), diskGuid, partitionArrayLBACount: partitionArrayLBACount, isBackupGPT: isBackupGPT);
        }

        private static void InjectWindowsPartitions(List<GPTPartition> partitions, ulong sectorSize, ulong blockSize, bool splitInHalf, ulong androidDesiredSpace = 4_294_967_296)
        {
            ulong firstUsableLBA = partitions.Last().FirstLBA;
            ulong lastUsableLBA = partitions.Last().LastLBA;

            if (lastUsableLBA % blockSize != 0)
            {
                lastUsableLBA -= lastUsableLBA % blockSize;
            }

            ulong usableLBACount = lastUsableLBA - firstUsableLBA + 1;
            ulong sixtyFourGigaBytes = 68_719_476_736 / sectorSize;

            ulong espLBACount = 65536; // ESP partition size
            ulong windowsLBACount;

            if (splitInHalf)
            {
                ulong androidOtherLUNLBAUsage = 8_679_372; // Size taken in Android by another LUN
                ulong totalAvailable = usableLBACount - espLBACount;
                windowsLBACount = Math.Max((totalAvailable - androidOtherLUNLBAUsage) / 2, sixtyFourGigaBytes);
            }
            else
            {
                ulong fourGigaBytes = androidDesiredSpace / sectorSize;
                windowsLBACount = Math.Max(usableLBACount - espLBACount - fourGigaBytes, sixtyFourGigaBytes);
            }

            ulong totalInjectedLBACount = espLBACount + windowsLBACount;
            ulong espFirstLBA = lastUsableLBA - totalInjectedLBACount + 1;
            ulong espLastLBA = espFirstLBA + espLBACount - 1;
            ulong windowsFirstLBA = espLastLBA + 1;
            ulong windowsLastLBA = windowsFirstLBA + windowsLBACount - 1;

            partitions.Add(new GPTPartition()
            {
                TypeGUID = Guid.Parse("c12a7328-f81f-11d2-ba4b-00a0c93ec93b"),
                UID = Guid.NewGuid(),
                FirstLBA = espFirstLBA,
                LastLBA = espLastLBA,
                Attributes = 0,
                Name = "EFI System Partition"
            });

            partitions.Add(new GPTPartition()
            {
                TypeGUID = Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7"),
                UID = Guid.NewGuid(),
                FirstLBA = windowsFirstLBA,
                LastLBA = windowsLastLBA,
                Attributes = 0,
                Name = "Windows ARM System"
            });
        }

        private static byte[] MakeGPT(ulong firstLBA, ulong lastLBA, ulong sectorSize, GPTPartition[] partitions, Guid diskGuid, ulong partitionArrayLBACount, bool isBackupGPT)
        {
            GPTHeader header = new GPTHeader
            {
                Signature = "EFI PART",
                Revision = 0x10000,
                Size = 92,
                CRC32 = 0, // Placeholder for CRC32 value to be calculated
                CurrentLBA = isBackupGPT ? lastLBA : firstLBA,
                BackupLBA = isBackupGPT ? firstLBA : lastLBA,
                FirstUsableLBA = firstLBA + 1,
                LastUsableLBA = lastLBA - 1,
                DiskGUID = diskGuid,
                PartitionArrayLBA = firstLBA + 1,
                PartitionEntryCount = (uint)partitions.Length,
                PartitionEntrySize = 128,
                PartitionArrayCRC32 = CalculateCRC32(partitions) // Placeholder for CRC32 calculation
            };

            return SerializeGPT(header, partitions, sectorSize);
        }

        private static uint CalculateCRC32(GPTPartition[] partitions)
        {
            // Implement CRC32 calculation for partitions
            return 0;  // Placeholder
        }

        private static byte[] SerializeGPT(GPTHeader header, GPTPartition[] partitions, ulong sectorSize)
        {
            // Implement serialization logic for GPT
            return new byte[0];  // Placeholder
        }
    }

    internal struct GPTPartition
    {
        public Guid TypeGUID;
        public Guid UID;
        public ulong FirstLBA;
        public ulong LastLBA;
        public ulong Attributes;
        public string Name;
    }

    internal struct GPTHeader
    {
        public string Signature;
        public uint Revision;
        public uint Size;
        public uint CRC32;
        public uint Reserved;
        public ulong CurrentLBA;
        public ulong BackupLBA;
        public ulong FirstUsableLBA;
        public ulong LastUsableLBA;
        public Guid DiskGUID;
        public ulong PartitionArrayLBA;
        public uint PartitionEntryCount;
        public uint PartitionEntrySize;
        public uint PartitionArrayCRC32;
    }
}
