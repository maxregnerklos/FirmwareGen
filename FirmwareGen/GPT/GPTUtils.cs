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
                throw new Exception("Unsupported Configuration, too many partitions to fit. File an issue");
            }

            ulong totalGPTLBACount = 1 /* GPT Header */ + partitionArrayLBACount /* Partition Table */;
            ulong lastUsableLBA = lastLBA - totalGPTLBACount;

            List<GPTPartition> partitions = new(defaultPartitionTable);
            partitions[^1].LastLBA = lastUsableLBA;

            if (androidDesiredSpace < 4_294_967_296)
            {
                throw new Exception("ERROR");
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

            ulong espLBACount = 65525 + 1024 + 1 /* Cluster Size Limit for FAT32 */;
            if (espLBACount % blockSize != 0)
            {
                espLBACount += blockSize - (espLBACount % blockSize);
            }

            ulong windowsLBACount;

            /* Strategy to reserve half for Android, half for Windows */
            if (splitInHalf)
            {
                ulong androidOtherLUNLBAUsage = 8_679_372 /* Size taken in Android by another LUN that counts towards Android space utilization */;
                windowsLBACount = (usableLBACount + androidOtherLUNLBAUsage - espLBACount) / 2;

                if (windowsLBACount < sixtyFourGigaBytes)
                {
                    windowsLBACount = sixtyFourGigaBytes;
                }

                // In the case of the 4GB for Android strategy, we cannot do this or we risk to get userdata < 4GB
                if (windowsLBACount % blockSize != 0)
                {
                    windowsLBACount += blockSize - (windowsLBACount % blockSize);
                }
            }
            /* Strategy to reserve 4GB for Android Only */
            else
            {
                ulong fourGigaBytes = androidDesiredSpace / sectorSize;
                windowsLBACount = usableLBACount - espLBACount - fourGigaBytes;

                if (windowsLBACount < sixtyFourGigaBytes)
                {
                    windowsLBACount = sixtyFourGigaBytes;
                }

                if (windowsLBACount % blockSize != 0)
                {
                    windowsLBACount -= windowsLBACount % blockSize;
                }
            }

            ulong totalInjectedLBACount = espLBACount + windowsLBACount;

            ulong espFirstLBA = lastUsableLBA - totalInjectedLBACount;
            ulong espLastLBA = espFirstLBA + espLBACount - 1;

            ulong windowsFirstLBA = espLastLBA + 1;
            ulong windowsLastLBA = espLastLBA + windowsLBACount;

            if (espFirstLBA % blockSize != 0)
            {
                ulong padding = blockSize - (espFirstLBA % blockSize);
                throw new Exception("ESPFirstLBA overflew block alignment by: " + padding);
            }

            if ((espLastLBA + 1) % blockSize != 0)
            {
                ulong padding = blockSize - ((espLastLBA + 1) % blockSize);
                throw new Exception("ESPLastLBA + 1 overflew block alignment by: " + padding);
            }

            if (windowsFirstLBA % blockSize != 0)
            {
                ulong padding = blockSize - (windowsFirstLBA % blockSize);
                throw new Exception("WindowsFirstLBA overflew block alignment by: " + padding);
            }

            if ((windowsLastLBA + 1) % blockSize != 0)
            {
                ulong padding = blockSize - ((windowsLastLBA + 1) % blockSize);
                throw new Exception("WindowsLastLBA + 1 overflew block alignment by: " + padding);
            }

            partitions.Add(new()
            {
                TypeGUID = new Guid("c12a7328-f81f-11d2-ba4b-00a0c93ec93b"),
                UID = new Guid("dec2832a-5f6c-430a-bd85-42551bce7b91"),
                FirstLBA = espFirstLBA,
                LastLBA = espLastLBA,
                Attributes = 0,
                Name = "esp"
            });

            partitions.Add(new()
            {
                TypeGUID = new Guid("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7"),
                UID = new Guid("92dee62d-ed67-4ec3-9daa-c9a4bce2c355"),
                FirstLBA = windowsFirstLBA,
                LastLBA = windowsLastLBA,
                Attributes = 0,
                Name = "win"
            });

            partitions[^3].LastLBA = espFirstLBA - 1;

            ConsoleColor ogColor = Console.ForegroundColor;

            ulong androidSpaceInBytes = (partitions[^3].LastLBA - partitions[^3].FirstLBA) * sectorSize;
            ulong windowsSpaceInBytes = (partitions[^1].LastLBA - partitions[^1].FirstLBA) * sectorSize;

            Console.WriteLine("Resulting Allocation after Computation, Compatibility Checks and Corrections:");
            Console.WriteLine();
            Console.WriteLine($"Android: {Math.Round(androidSpaceInBytes / (double)(1024 * 1024 * 1024), 2)}GB ({Math.Round(androidSpaceInBytes / (double)(1000 * 1000 * 1000), 2)}GiB)");
            Console.WriteLine($"Windows: {Math.Round(windowsSpaceInBytes / (double)(1024 * 1024 * 1024), 2)}GB ({Math.Round(windowsSpaceInBytes / (double)(1000 * 1000 * 1000), 2)}GiB)");
            Console.WriteLine();

            Console.WriteLine("Resulting parted commands:");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine();
            Console.WriteLine($"mkpart {partitions[^3].Name} ext4 {partitions[^3].FirstLBA}s {partitions[^3].LastLBA}s");
            Console.WriteLine();
            Console.WriteLine($"mkpart {partitions[^2].Name} fat32 {partitions[^2].FirstLBA}s {partitions[^2].LastLBA}s");
            Console.WriteLine();
            //Console.WriteLine($"mkpart {partitions[^1].Name} ntfs {partitions[^1].FirstLBA}s {Math.Truncate(partitions[^1].LastLBA * sectorSize / (double)(1000 * 1000 * 1000))}GB");
            Console.WriteLine($"mkpart {partitions[^1].Name} ntfs {partitions[^1].FirstLBA}s {partitions[^1].LastLBA}s");
            Console.WriteLine();

            Console.ForegroundColor = ogColor;
        }

        private static byte[] MakeGPT(ulong firstLBA, ulong lastLBA, ulong sectorSize, GPTPartition[] partitions, Guid diskGuid, ulong partitionArrayLBACount = 4, bool isBackupGPT = false)
        {
            // -------------------
            // 0: Reserved/MBR
            // -------------------
            // 1: GPT Header
            // -------------------
            // 2: Partition Table
            // 3: Partition Table
            // 4: Partition Table
            // 5: Partition Table
            // -------------------
            // 6: First Usable LBA
            // ...
            // -5: Last Usable LBA
            // -------------------
            // -4: Partition Table
            // -3: Partition Table
            // -2: Partition Table
            // -1: Partition Table
            // -------------------
            // -0: Backup GPT Header
            // -------------------

            ulong totalGPTLBACount = 1 /* GPT Header */ + partitionArrayLBACount /* Partition Table */;

            ulong firstUsableLBA = firstLBA + totalGPTLBACount;
            ulong lastUsableLBA = lastLBA - totalGPTLBACount;

            uint partitionEntryCount;

            if ((uint)partitions.Length > 128)
            {
                throw new Exception("Unsupported Configuration, too many partitions than supported, please file an issue.");
            }
            else
            {
                partitionEntryCount = (uint)partitions.Length > 64 ? 128 : (uint)partitions.Length > 32 ? 64 : (uint)32;
            }

            GPTHeader header = new()
            {
                Signature = "EFI PART",
                Revision = 0x10000,
                Size = 92,
                CRC32 = 0,
                Reserved = 0,
                CurrentLBA = isBackupGPT ? lastLBA : firstLBA,
                BackupLBA = isBackupGPT ? firstLBA : lastLBA,
                FirstUsableLBA = firstUsableLBA,
                LastUsableLBA = lastUsableLBA,
                DiskGUID = diskGuid,
                PartitionArrayLBA = isBackupGPT ? lastLBA - totalGPTLBACount + 1 : firstLBA + 1,
                PartitionEntryCount = partitionEntryCount,
                PartitionEntrySize = 128,
                PartitionArrayCRC32 = 0
            };

            List<byte> partitionTableBuffer = new();
            for (int i = 0; i < partitions.Length; i++)
            {
                partitionTableBuffer.AddRange(partitions[i].TypeGUID.ToByteArray());
                partitionTableBuffer.AddRange(partitions[i].UID.ToByteArray());
                partitionTableBuffer.AddRange(BitConverter.GetBytes(partitions[i].FirstLBA));
                partitionTableBuffer.AddRange(BitConverter.GetBytes(partitions[i].LastLBA));
                partitionTableBuffer.AddRange(BitConverter.GetBytes(partitions[i].Attributes));
                partitionTableBuffer.AddRange(Encoding.Unicode.GetBytes(partitions[i].Name));
                partitionTableBuffer.AddRange(new byte[(header.PartitionEntrySize * (ulong)(long)(i + 1)) - (ulong)(long)partitionTableBuffer.Count]);
            }
            partitionTableBuffer.AddRange(new byte[(header.PartitionEntrySize * header.PartitionEntryCount) - (ulong)(long)partitionTableBuffer.Count]);

            uint partitionTableCRC32 = CRC32.Compute(partitionTableBuffer.ToArray(), 0, (uint)partitionTableBuffer.Count);
            header.PartitionArrayCRC32 = partitionTableCRC32;

            byte[] headerBuffer =
            {
                Encoding.ASCII.GetBytes(header.Signature),
                BitConverter.GetBytes(header.Revision),
                BitConverter.GetBytes(header.Size),
                BitConverter.GetBytes(header.CRC32),
                BitConverter.GetBytes(header.Reserved),
                BitConverter.GetBytes(header.CurrentLBA),
                BitConverter.GetBytes(header.BackupLBA),
                BitConverter.GetBytes(header.FirstUsableLBA),
                BitConverter.GetBytes(header.LastUsableLBA),
                header.DiskGUID.ToByteArray(),
                BitConverter.GetBytes(header.PartitionArrayLBA),
                BitConverter.GetBytes(header.PartitionEntryCount),
                BitConverter.GetBytes(header.PartitionEntrySize),
                BitConverter.GetBytes(header.PartitionArrayCRC32),
            };

            header.CRC32 = CRC32.Compute(headerBuffer, 0, (uint)headerBuffer.Length);
            byte[] bytes = BitConverter.GetBytes(header.CRC32);

            headerBuffer[16] = bytes[0];
            headerBuffer[17] = bytes[1];
            headerBuffer[18] = bytes[2];
            headerBuffer[19] = bytes[3];

            byte[] headerPaddingBuffer = new byte[(int)(sectorSize - (uint)headerBuffer.Length)];
            byte[] partitionTablePaddingBuffer = new byte[(int)((partitionArrayLBACount * sectorSize) - (uint)partitionTableBuffer.Count)];

            List<byte> gptBuffer = new();
            if (isBackupGPT)
            {
                gptBuffer.AddRange(partitionTableBuffer);
                gptBuffer.AddRange(partitionTablePaddingBuffer);

                gptBuffer.AddRange(headerBuffer);
                gptBuffer.AddRange(headerPaddingBuffer);
            }
            else
            {
                gptBuffer.AddRange(headerBuffer);
                gptBuffer.AddRange(headerPaddingBuffer);

                gptBuffer.AddRange(partitionTableBuffer);
                gptBuffer.AddRange(partitionTablePaddingBuffer);
            }

            return gptBuffer.ToArray();
        }
    }
}
