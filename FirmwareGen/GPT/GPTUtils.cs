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

            const ulong partitionArrayLBACount = 4;
            if ((ulong)defaultPartitionTable.Length * 128 > partitionArrayLBACount * sectorSize)
            {
                throw new Exception("Unsupported Configuration, too many partitions to fit. File an issue.");
            }

            ulong totalGPTLBACount = 1 + partitionArrayLBACount; // GPT Header + Partition Table
            ulong lastUsableLBA = lastLBA - totalGPTLBACount;

            List<GPTPartition> partitions = new List<GPTPartition>(defaultPartitionTable);
            partitions[^1].LastLBA = lastUsableLBA;

            if (androidDesiredSpace < 4_294_967_296)
            {
                throw new Exception("ERROR: Android desired space cannot be less than 4GB.");
            }

            InjectWindowsPartitions(partitions, sectorSize, 4, splitInHalf, androidDesiredSpace);

            return SerializeGPT(firstLBA, lastLBA, sectorSize, partitions.ToArray(), diskGuid, partitionArrayLBACount, isBackupGPT);
        }

        private static void InjectWindowsPartitions(List<GPTPartition> partitions, ulong sectorSize, ulong blockSize, bool splitInHalf, ulong androidDesiredSpace)
        {
            ulong firstUsableLBA = partitions[^1].FirstLBA;
            ulong lastUsableLBA = partitions[^1].LastLBA;
            AdjustToBlockSize(ref lastUsableLBA, blockSize);

            ulong usableLBACount = lastUsableLBA - firstUsableLBA + 1;
            const ulong sixtyFourGigaBytes = 68_719_476_736 / sectorSize;
            ulong espLBACount = CalculateESPBlockSize(65536, blockSize); // Standard ESP partition size plus some buffer

            ulong windowsLBACount = CalculateWindowsPartitionSize(usableLBACount, espLBACount, androidDesiredSpace, splitInHalf, sixtyFourGigaBytes, blockSize);

            ulong totalInjectedLBACount = espLBACount + windowsLBACount;
            ulong espFirstLBA = lastUsableLBA - totalInjectedLBACount + 1;
            ulong espLastLBA = espFirstLBA + espLBACount - 1;
            ulong windowsFirstLBA = espLastLBA + 1;
            ulong windowsLastLBA = windowsFirstLBA + windowsLBACount - 1;

            ValidateBlockAlignment(new ulong[] { espFirstLBA, espLastLBA + 1, windowsFirstLBA, windowsLastLBA + 1 }, blockSize);

            partitions.Add(CreatePartition("EFI System Partition", "c12a7328-f81f-11d2-ba4b-00a0c93ec93b", espFirstLBA, espLastLBA));
            partitions.Add(CreatePartition("Windows ARM System", "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", windowsFirstLBA, windowsLastLBA));
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

            Console.WriteLine("Resulting Allocation after Computation, Compatibility Checks and Corrections:");
            Console.WriteLine();
            Console.WriteLine($"Android: {Math.Round(androidSpaceInBytes / (double)(1024 * 1024 * 1024), 2)}GB ({Math.Round(androidSpaceInBytes / (double)(1000 * 1000 * 1000), 2)}GiB)");
            Console.WriteLine($"Windows: {Math.Round(windowsSpaceInBytes / (double)(1024 * 1024 * 1024), 2)}GB ({Math.Round(windowsSpaceInBytes / (double)(1000 * 1000 * 1000), 2)}GiB)");
            Console.WriteLine();

            Console.WriteLine("Resulting parted commands:");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine();
            Console.WriteLine($"mkpart {Partitions[^3].Name} ext4 {Partitions[^3].FirstLBA}s {Partitions[^3].LastLBA}s");
            Console.WriteLine();
            Console.WriteLine($"mkpart {Partitions[^2].Name} fat32 {Partitions[^2].FirstLBA}s {Partitions[^2].LastLBA}s");
            Console.WriteLine();
            //Console.WriteLine($"mkpart {Partitions[^1].Name} ntfs {Partitions[^1].FirstLBA}s {Math.Truncate(Partitions[^1].LastLBA * SectorSize / (double)(1000 * 1000 * 1000))}GB");
            Console.WriteLine($"mkpart {Partitions[^1].Name} ntfs {Partitions[^1].FirstLBA}s {Partitions[^1].LastLBA}s");
            Console.WriteLine();

            Console.ForegroundColor = ogColor;
        }

        private static byte[] MakeGPT(ulong FirstLBA, ulong LastLBA, ulong SectorSize, GPTPartition[] Partitions, Guid DiskGuid, ulong PartitionArrayLBACount = 4, bool IsBackupGPT = false)
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

            ulong TotalGPTLBACount = 1 /* GPT Header */ + PartitionArrayLBACount /* Partition Table */;

            ulong FirstUsableLBA = FirstLBA + TotalGPTLBACount;
            ulong LastUsableLBA = LastLBA - TotalGPTLBACount;

            uint PartitionEntryCount;

            if ((uint)Partitions.Length > 128)
            {
                throw new Exception("Unsupported Configuration, too many partitions than supported, please file an issue.");
            }
            else
            {
                PartitionEntryCount = (uint)Partitions.Length > 64 ? 128 : (uint)Partitions.Length > 32 ? 64 : (uint)32;
            }

            GPTHeader Header = new()
            {
                Signature = "EFI PART",
                Revision = 0x10000,
                Size = 92,
                CRC32 = 0,
                Reserved = 0,
                CurrentLBA = IsBackupGPT ? LastLBA : FirstLBA,
                BackupLBA = IsBackupGPT ? FirstLBA : LastLBA,
                FirstUsableLBA = FirstUsableLBA,
                LastUsableLBA = LastUsableLBA,
                DiskGUID = DiskGuid,
                PartitionArrayLBA = IsBackupGPT ? LastLBA - TotalGPTLBACount + 1 : FirstLBA + 1,
                PartitionEntryCount = PartitionEntryCount,
                PartitionEntrySize = 128,
                PartitionArrayCRC32 = 0
            };

            List<byte> PartitionTableBuffer = [];
            for (int i = 0; i < Partitions.Length; i++)
            {
                PartitionTableBuffer.AddRange(Partitions[i].TypeGUID.ToByteArray());
                PartitionTableBuffer.AddRange(Partitions[i].UID.ToByteArray());
                PartitionTableBuffer.AddRange(BitConverter.GetBytes(Partitions[i].FirstLBA));
                PartitionTableBuffer.AddRange(BitConverter.GetBytes(Partitions[i].LastLBA));
                PartitionTableBuffer.AddRange(BitConverter.GetBytes(Partitions[i].Attributes));
                PartitionTableBuffer.AddRange(Encoding.Unicode.GetBytes(Partitions[i].Name));
                PartitionTableBuffer.AddRange(new byte[(Header.PartitionEntrySize * (ulong)(long)(i + 1)) - (ulong)(long)PartitionTableBuffer.Count]);
            }
            PartitionTableBuffer.AddRange(new byte[(Header.PartitionEntrySize * Header.PartitionEntryCount) - (ulong)(long)PartitionTableBuffer.Count]);

            uint PartitionTableCRC32 = CRC32.Compute([.. PartitionTableBuffer], 0, (uint)PartitionTableBuffer.Count);
            Header.PartitionArrayCRC32 = PartitionTableCRC32;

            byte[] HeaderBuffer =
            [
                .. Encoding.ASCII.GetBytes(Header.Signature),
                .. BitConverter.GetBytes(Header.Revision),
                .. BitConverter.GetBytes(Header.Size),
                .. BitConverter.GetBytes(Header.CRC32),
                .. BitConverter.GetBytes(Header.Reserved),
                .. BitConverter.GetBytes(Header.CurrentLBA),
                .. BitConverter.GetBytes(Header.BackupLBA),
                .. BitConverter.GetBytes(Header.FirstUsableLBA),
                .. BitConverter.GetBytes(Header.LastUsableLBA),
                .. Header.DiskGUID.ToByteArray(),
                .. BitConverter.GetBytes(Header.PartitionArrayLBA),
                .. BitConverter.GetBytes(Header.PartitionEntryCount),
                .. BitConverter.GetBytes(Header.PartitionEntrySize),
                .. BitConverter.GetBytes(Header.PartitionArrayCRC32),
            ];

            Header.CRC32 = CRC32.Compute(HeaderBuffer, 0, (uint)HeaderBuffer.Length);
            byte[] bytes = BitConverter.GetBytes(Header.CRC32);

            HeaderBuffer[16] = bytes[0];
            HeaderBuffer[17] = bytes[1];
            HeaderBuffer[18] = bytes[2];
            HeaderBuffer[19] = bytes[3];

            byte[] HeaderPaddingBuffer = new byte[(int)(SectorSize - (uint)HeaderBuffer.Length)];
            byte[] PartitionTablePaddingBuffer = new byte[(int)((PartitionArrayLBACount * SectorSize) - (uint)PartitionTableBuffer.Count)];

            List<byte> GPTBuffer = [];
            if (IsBackupGPT)
            {
                GPTBuffer.AddRange(PartitionTableBuffer);
                GPTBuffer.AddRange(PartitionTablePaddingBuffer);

                GPTBuffer.AddRange(HeaderBuffer);
                GPTBuffer.AddRange(HeaderPaddingBuffer);
            }
            else
            {
                GPTBuffer.AddRange(HeaderBuffer);
                GPTBuffer.AddRange(HeaderPaddingBuffer);

                GPTBuffer.AddRange(PartitionTableBuffer);
                GPTBuffer.AddRange(PartitionTablePaddingBuffer);
            }

            return [.. GPTBuffer];
        }
    }
}
