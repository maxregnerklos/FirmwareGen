using System;
using System.Collections.Generic;
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

            return CreateGPT(firstLBA, lastLBA, sectorSize, partitions.ToArray(), diskGuid, partitionArrayLBACount, isBackupGPT);
        }

        private static void InjectWindowsPartitions(List<GPTPartition> partitions, ulong sectorSize, ulong blockSize, bool splitInHalf, ulong androidDesiredSpace)
        {
            ulong firstUsableLBA = partitions[^1].FirstLBA;
            ulong lastUsableLBA = partitions[^1].LastLBA;

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
                ulong totalAvailable = usableLBACount - androidOtherLUNLBAUsage;
                windowsLBACount = Math.Max((totalAvailable - espLBACount) / 2, sixtyFourGigaBytes);
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
        } // Ensure this closing bracket exists

        private static byte[] CreateGPT(ulong firstLBA, ulong lastLBA, ulong sectorSize, GPTPartition[] partitions, Guid diskGuid, ulong partitionArrayLBACount, bool isBackupGPT)
        {
            // Serialization logic to construct the GPT binary data
            // Ensure all methods are properly closed with brackets
        } // Ensure this closing bracket exists
    }
}
