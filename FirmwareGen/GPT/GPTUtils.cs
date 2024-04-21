using System;
using System.Collections.Generic;
using System.Text;

namespace FirmwareGen.GPT
{
    public class GPTUtils
    {
        public static byte[] MakeGPT(ulong diskSize, ulong sectorSize, GPTPartition[] defaultPartitionTable, Guid diskGuid, bool isBackupGpt = false, bool splitInHalf = true, ulong androidDesiredSpace = 4_294_967_296)
        {
            ulong firstLba = 1;
            ulong lastLba = (diskSize / sectorSize) - 1;
            const ulong partitionArrayLbaCount = 4;

            if ((ulong)defaultPartitionTable.Length * 128 > partitionArrayLbaCount * sectorSize)
            {
                throw new Exception("Unsupported Configuration, too many partitions to fit. File an issue.");
            }

            ulong totalGptLbaCount = 1 + partitionArrayLbaCount; // GPT Header + Partition Table
            ulong lastUsableLba = lastLba - totalGptLbaCount;

            List<GPTPartition> partitions = new List<GPTPartition>(defaultPartitionTable);
            partitions[^1].LastLba = lastUsableLba;

            if (androidDesiredSpace < 4_294_967_296)
            {
                throw new Exception("ERROR: Android desired space cannot be less than 4GB.");
            }

            InjectWindowsPartitions(partitions, sectorSize, 4, splitInHalf, androidDesiredSpace);

            return SerializeGPT(firstLba, lastLba, sectorSize, partitions.ToArray(), diskGuid, partitionArrayLbaCount, isBackupGpt);
        }

        private static void InjectWindowsPartitions(List<GPTPartition> partitions, ulong sectorSize, ulong blockSize, bool splitInHalf, ulong androidDesiredSpace)
        {
            ulong firstUsableLba = partitions[^1].FirstLba;
            ulong lastUsableLba = partitions[^1].LastLba;

            if (lastUsableLba % blockSize != 0)
            {
                lastUsableLba -= lastUsableLba % blockSize;
            }

            ulong usableLbaCount = lastUsableLba - firstUsableLba + 1;
            ulong espLbaCount = CalculateEspBlockSize(65536, blockSize); // Standard ESP partition size

            ulong windowsLbaCount = CalculateWindowsPartitionSize(usableLbaCount, espLbaCount, androidDesiredSpace, splitInHalf, sectorSize, blockSize);

            ulong totalInjectedLbaCount = espLbaCount + windowsLbaCount;
            ulong espFirstLba = lastUsableLba - totalInjectedLbaCount + 1;
            ulong espLastLba = espFirstLba + espLbaCount - 1;
            ulong windowsFirstLba = espLastLba + 1;
            ulong windowsLastLba = windowsFirstLba + windowsLbaCount - 1;

            ValidateBlockAlignment(new[] { espFirstLba, espLastLba + 1, windowsFirstLba, windowsLastLba + 1 }, blockSize);

            partitions.Add(CreatePartition("EFI System Partition", "c12a7328-f81f-11d2-ba4b-00a0c93ec93b", espFirstLba, espLastLba));
            partitions.Add(CreatePartition("Windows ARM System", "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", windowsFirstLba, windowsLastLba));
        }

        private static ulong CalculateWindowsPartitionSize(ulong usableLbaCount, ulong espLbaCount, ulong androidDesiredSpace, bool splitInHalf, ulong sectorSize, ulong blockSize)
        {
            ulong minSize = 68_719_476_736 / sectorSize; // 64 GB minimum for Windows
            ulong windowsLbaCount;
            if (splitInHalf)
            {
                ulong halfAvailable = (usableLbaCount - espLbaCount) / 2;
                windowsLbaCount = Math.Max(halfAvailable, minSize);
            }
            else
            {
                ulong spaceForAndroid = androidDesiredSpace / sectorSize;
                windowsLbaCount = Math.Max(usableLbaCount - espLbaCount - spaceForAndroid, minSize);
            }
            AdjustToBlockSize(ref windowsLbaCount, blockSize);
            return windowsLbaCount;
        }

        private static void ValidateBlockAlignment(ulong[] blockStarts, ulong blockSize)
        {
            foreach (var start in blockStarts)
            {
                if (start % blockSize != 0)
                {
                    throw new Exception($"Block alignment error at LBA: {start}");
                }
            }
        }

        private static ulong CalculateEspBlockSize(ulong baseSize, ulong blockSize)
        {
            return ((baseSize + blockSize - 1) / blockSize) * blockSize;
        }

        private static GPTPartition CreatePartition(string name, string typeGuid, ulong firstLba, ulong lastLba)
        {
            return new GPTPartition
            {
                Name = name,
                TypeGuid = Guid.Parse(typeGuid),
                FirstLba = firstLba,
                LastLba = lastLba,
                Attributes = 0,
                UID = Guid.NewGuid()
            };
        }

        private static byte[] SerializeGPT(ulong firstLba, ulong lastLba, ulong sectorSize, GPTPartition[] partitions, Guid diskGuid, ulong partitionArrayLbaCount, bool isBackupGpt)
        {
            // Placeholder for actual GPT serialization logic
            return Array.Empty<byte>(); // This should be replaced with actual GPT serialization code
        }
    }

    public struct GPTPartition
    {
        public Guid TypeGuid;
        public Guid UID;
        public ulong FirstLba;
        public ulong LastLba;
        public ulong Attributes;
        public string Name;
    }
}
