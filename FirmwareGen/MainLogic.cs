using FirmwareGen.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Diagnostics;

namespace FirmwareGen
{
            public static class MainLogic
            {
                public static async Task<bool> VerifyAllComponentsArePresentAsync()
                {
                    string[] requiredComponents = { "wimlib-imagex.exe", "Img2Ffu.exe", "DriverUpdater.exe" };

                    foreach (var component in requiredComponents)
                    {
                        if (!File.Exists(component))
                        {
                            await Logging.LogAsync($"Component not found: {component}", Logging.LoggingLevel.Error);
                            return false;
                        }
                    }

                    return true;
                }

                public static async Task GenerateWindowsFFUAsync(GenerateWindowsFFUOptions options)
                {
                    const string wimlib = "wimlib-imagex.exe";
                    const string Img2Ffu = "Img2Ffu.exe";
                    const string DriverUpdater = "DriverUpdater.exe";
                    const string SystemPartition = "Y:";

                    DeviceProfile deviceProfile = await XmlUtils.DeserializeAsync<DeviceProfile>(options.DeviceProfile);

                    string TmpVHD = await CommonLogic.GetBlankVHDAsync(deviceProfile);
                    string DiskId = await VolumeUtils.MountVirtualHardDiskAsync(TmpVHD, false);
                    string VHDLetter = await VolumeUtils.GetVirtualHardDiskLetterFromDiskIDAsync(DiskId);

                    await VolumeUtils.ApplyWindowsImageFromDVDAsync(wimlib, options.WindowsDVD, options.WindowsIndex, VHDLetter);
                    await VolumeUtils.PerformSlabOptimizationAsync(VHDLetter);
                    await VolumeUtils.ApplyCompactFlagsToImageAsync(VHDLetter);
                    await VolumeUtils.MountSystemPartitionAsync(DiskId, SystemPartition);
                    await VolumeUtils.ConfigureBootManagerAsync(VHDLetter, SystemPartition);
                    await VolumeUtils.UnmountSystemPartitionAsync(DiskId, SystemPartition);

                    if (deviceProfile.SupplementaryBCDCommands.Length > 0)
                    {
                        await VolumeUtils.MountSystemPartitionAsync(DiskId, SystemPartition);

                        await Logging.LogAsync("Configuring supplemental boot");
                        foreach (string command in deviceProfile.SupplementaryBCDCommands)
                        {
                            await VolumeUtils.RunProgramAsync("bcdedit.exe", $"{$@"/store {SystemPartition}\EFI\Microsoft\Boot\BCD "}{command}");
                        }

                        await VolumeUtils.UnmountSystemPartitionAsync(DiskId, SystemPartition);
                    }

                    await Logging.LogAsync("Adding drivers");
                    await VolumeUtils.RunProgramAsync(DriverUpdater, $@"-d ""{options.DriverPack}{deviceProfile.DriverDefinitionPath}"" -r ""{options.DriverPack}"" -p ""{VHDLetter}""");

                    if (IsARM64())
                    {
                        await ApplyARM64OptimizationsAsync(VHDLetter);
                    }

                    await VolumeUtils.DismountVirtualHardDiskAsync(TmpVHD);

                    await Logging.LogAsync("Making FFU");
                    await VolumeUtils.RunProgramAsync(Img2Ffu, $@"-i {TmpVHD} -f ""{options.Output}\{deviceProfile.FFUFileName}"" -c {deviceProfile.DiskSectorSize * 4} -s {deviceProfile.DiskSectorSize} -p ""{string.Join(";", deviceProfile.PlatformIDs)}"" -o {options.WindowsVer} -b 4000");

                    await Logging.LogAsync("Deleting Temp VHD");
                    File.Delete(TmpVHD);
                }

                private static bool IsARM64()
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                    {
                        if (key != null)
                        {
                            var processorArchitecture = key.GetValue("PROCESSOR_ARCHITECTURE") as string;
                            return string.Equals(processorArchitecture, "ARM64", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    return false;
                }

                private static async Task ApplyARM64OptimizationsAsync(string vhdLetter)
                {
                    await Logging.LogAsync("Applying ARM64-specific optimizations");

                    await VolumeUtils.RunProgramAsync("dism", $@"/Image:{vhdLetter} /Enable-Feature /FeatureName:ARM64EC /All");

                    await VolumeUtils.RunProgramAsync("powercfg", $@"/S SCHEME_MAX /Path {vhdLetter}\Windows\System32\GroupPolicy\Machine\Scripts");

                    string regFile = Path.Combine(Path.GetTempPath(), "ARM64_optimizations.reg");
                    File.WriteAllText(regFile, @"Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management]
""FeatureSettingsOverride""=dword:00000003
""FeatureSettingsOverrideMask""=dword:00000003

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]
""HwSchMode""=dword:00000002");

                    await VolumeUtils.RunProgramAsync("reg", $@"load HKLM\ARM64TEMP {vhdLetter}\Windows\System32\config\SYSTEM");
                    await VolumeUtils.RunProgramAsync("reg", $@"import {regFile}");
                    await VolumeUtils.RunProgramAsync("reg", @"unload HKLM\ARM64TEMP");

                    File.Delete(regFile);
                }
            }
}
