using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LlamaVulkanLauncher
{
    internal sealed class GpuDevice
    {
        public string Id;
        public string Name;
        public long MemoryBytes;
    }

    internal sealed class HardwareInfo
    {
        public string CpuName;
        public int PhysicalCores;
        public int LogicalProcessors;
        public long TotalMemoryBytes;
        public long AvailableMemoryBytes;
        public int MemoryLoadPercent;
        public string GpuName;
        public long DedicatedVramBytes;
        public GpuDevice[] VulkanDevices;
    }

    internal static class SystemResources
    {
        private const int RelationProcessorCore = 0;

        public static HardwareInfo Query()
        {
            HardwareInfo info = new HardwareInfo();
            info.CpuName = ReadCpuName();
            info.LogicalProcessors = Math.Max(1, Environment.ProcessorCount);
            info.PhysicalCores = ReadPhysicalCoreCount();
            if (info.PhysicalCores <= 0)
            {
                info.PhysicalCores = info.LogicalProcessors;
            }

            MEMORYSTATUSEX status = new MEMORYSTATUSEX();
            if (NativeGlobalMemoryStatusEx(status))
            {
                info.TotalMemoryBytes = (long)status.ullTotalPhys;
                info.AvailableMemoryBytes = (long)status.ullAvailPhys;
                info.MemoryLoadPercent = (int)status.dwMemoryLoad;
            }

            GpuDevice gpu = ReadPrimaryGpu();
            if (gpu != null)
            {
                info.GpuName = gpu.Name;
                info.DedicatedVramBytes = gpu.MemoryBytes;
            }
            else
            {
                info.GpuName = "";
                info.DedicatedVramBytes = 0;
            }
            info.VulkanDevices = new GpuDevice[0];

            return info;
        }

        public static void MergeVulkanDevices(HardwareInfo info, GpuDevice[] devices)
        {
            if (info == null)
            {
                return;
            }
            if (devices == null)
            {
                devices = new GpuDevice[0];
            }
            info.VulkanDevices = devices;

            GpuDevice best = null;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i] == null)
                {
                    continue;
                }
                if (best == null || devices[i].MemoryBytes > best.MemoryBytes)
                {
                    best = devices[i];
                }
            }
            if (best == null)
            {
                return;
            }
            if (info.DedicatedVramBytes <= 0 && best.MemoryBytes > 0)
            {
                info.DedicatedVramBytes = best.MemoryBytes;
            }
            if (string.IsNullOrEmpty(info.GpuName) && !string.IsNullOrEmpty(best.Name))
            {
                info.GpuName = best.Name;
            }
        }

        public static string FirstVulkanId(HardwareInfo info)
        {
            if (info == null || info.VulkanDevices == null)
            {
                return "";
            }
            for (int i = 0; i < info.VulkanDevices.Length; i++)
            {
                if (info.VulkanDevices[i] != null && !string.IsNullOrEmpty(info.VulkanDevices[i].Id))
                {
                    return info.VulkanDevices[i].Id;
                }
            }
            return "";
        }

        public static bool HasVulkanId(HardwareInfo info, string id)
        {
            if (info == null || info.VulkanDevices == null || string.IsNullOrEmpty(id))
            {
                return false;
            }
            for (int i = 0; i < info.VulkanDevices.Length; i++)
            {
                if (info.VulkanDevices[i] != null
                    && string.Equals(info.VulkanDevices[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static int SuggestOfficeThreads(HardwareInfo info)
        {
            int physical = info != null && info.PhysicalCores > 0
                ? info.PhysicalCores
                : Math.Max(1, Environment.ProcessorCount);
            // 辦公並行的重點是把核心留給前台程式：
            // 6 核以下的機器（例如 i5-9500）只給 2 條，體感最順。
            if (physical >= 12)
            {
                return physical - 4;
            }
            if (physical >= 8)
            {
                return physical - 3;
            }
            if (physical >= 6)
            {
                return 2;
            }
            if (physical >= 4)
            {
                return 2;
            }
            if (physical >= 2)
            {
                return physical - 1;
            }
            return 1;
        }

        public static int SuggestPerfThreads(HardwareInfo info)
        {
            if (info != null && info.PhysicalCores > 0)
            {
                return info.PhysicalCores;
            }
            return Math.Max(1, Environment.ProcessorCount);
        }

        public static long SuggestOfficeReserveBytes(HardwareInfo info)
        {
            double totalGb = info != null ? BytesToGb(info.TotalMemoryBytes) : 0;
            // 門檻略低於標示容量：作業系統辨識到的記憶體會少於模組標示值
            // （例如 48 GB 實際約 47.7 GB、32 GB 約 31.8 GB），
            // 用整數門檻會讓機器掉到低一級的設定。
            if (totalGb >= 46)
            {
                return 12L * 1024L * 1024L * 1024L;
            }
            if (totalGb >= 15)
            {
                return 8L * 1024L * 1024L * 1024L;
            }
            if (totalGb >= 7.5)
            {
                return 4L * 1024L * 1024L * 1024L;
            }
            return 2L * 1024L * 1024L * 1024L;
        }

        public static double BytesToGb(long bytes)
        {
            if (bytes <= 0)
            {
                return 0;
            }
            return bytes / 1073741824.0;
        }

        public static string FormatGb(long bytes)
        {
            return BytesToGb(bytes).ToString("0.0") + " GB";
        }

        public static int MapPrioToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return 0;
            }
            string v = token.Trim();
            if (string.Equals(v, "low", StringComparison.OrdinalIgnoreCase) || v == "-1")
            {
                return -1;
            }
            if (string.Equals(v, "medium", StringComparison.OrdinalIgnoreCase) || v == "1")
            {
                return 1;
            }
            if (string.Equals(v, "high", StringComparison.OrdinalIgnoreCase) || v == "2")
            {
                return 2;
            }
            return 0;
        }

        public static string NormalizePrio(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "";
            }
            string v = token.Trim();
            if (v.StartsWith("low", StringComparison.OrdinalIgnoreCase) || v == "-1")
            {
                return "low";
            }
            if (v.StartsWith("normal", StringComparison.OrdinalIgnoreCase) || v == "0")
            {
                return "normal";
            }
            if (v.StartsWith("medium", StringComparison.OrdinalIgnoreCase) || v == "1")
            {
                return "medium";
            }
            if (v.StartsWith("high", StringComparison.OrdinalIgnoreCase) || v == "2")
            {
                return "high";
            }
            if (v.StartsWith("省略", StringComparison.Ordinal))
            {
                return "";
            }
            return "";
        }

        private static string ReadCpuName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ProcessorNameString");
                        if (value != null)
                        {
                            return value.ToString().Trim();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return "未知處理器";
        }

        private static int ReadPhysicalCoreCount()
        {
            uint length = 0;
            NativeGetLogicalProcessorInformation(IntPtr.Zero, ref length);
            if (length == 0)
            {
                return Environment.ProcessorCount;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!NativeGetLogicalProcessorInformation(buffer, ref length))
                {
                    return Environment.ProcessorCount;
                }

                int entrySize = IntPtr.Size == 8 ? 32 : 24;
                int count = 0;
                int offset = 0;
                while (offset + entrySize <= (int)length)
                {
                    int relationship = Marshal.ReadInt32(buffer, offset + IntPtr.Size);
                    if (relationship == RelationProcessorCore)
                    {
                        count++;
                    }
                    offset += entrySize;
                }
                return count > 0 ? count : Environment.ProcessorCount;
            }
            catch (Exception)
            {
                return Environment.ProcessorCount;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static GpuDevice ReadPrimaryGpu()
        {
            GpuDevice dxgi = ReadPrimaryGpuDxgi();
            if (dxgi != null && dxgi.MemoryBytes > 0)
            {
                return dxgi;
            }
            GpuDevice registry = ReadPrimaryGpuRegistry();
            if (registry != null && registry.MemoryBytes > 0)
            {
                return registry;
            }
            return dxgi != null ? dxgi : registry;
        }

        private static GpuDevice ReadPrimaryGpuDxgi()
        {
            IDXGIFactory factory = null;
            try
            {
                Guid iid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                int hr = NativeCreateDxgiFactory(ref iid, out factory);
                if (hr != 0 || factory == null)
                {
                    return null;
                }

                GpuDevice best = null;
                uint index = 0;
                while (true)
                {
                    IDXGIAdapter adapter = null;
                    hr = factory.EnumAdapters(index, out adapter);
                    if (hr != 0 || adapter == null)
                    {
                        break;
                    }
                    try
                    {
                        DXGI_ADAPTER_DESC desc;
                        adapter.GetDesc(out desc);
                        if (!IsSoftwareGpu(desc.Description, desc.VendorId))
                        {
                            long vram = ToSignedSize(desc.DedicatedVideoMemory);
                            if (best == null || vram > best.MemoryBytes)
                            {
                                GpuDevice gpu = new GpuDevice();
                                gpu.Id = "";
                                gpu.Name = desc.Description != null ? desc.Description.Trim() : "";
                                gpu.MemoryBytes = vram;
                                best = gpu;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                    index++;
                    if (index > 16)
                    {
                        break;
                    }
                }
                return best;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (factory != null)
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }

        private static GpuDevice ReadPrimaryGpuRegistry()
        {
            GpuDevice best = null;
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (root == null)
                    {
                        return null;
                    }
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i] == null || names[i].Length != 4)
                        {
                            continue;
                        }
                        using (RegistryKey card = root.OpenSubKey(names[i]))
                        {
                            if (card == null)
                            {
                                continue;
                            }
                            string desc = card.GetValue("DriverDesc") as string;
                            if (string.IsNullOrEmpty(desc) || IsSoftwareGpu(desc, 0))
                            {
                                continue;
                            }
                            object raw = card.GetValue("HardwareInformation.qwMemorySize");
                            long vram = 0;
                            if (raw is long)
                            {
                                vram = (long)raw;
                            }
                            else if (raw is ulong)
                            {
                                vram = (long)(ulong)raw;
                            }
                            else if (raw is byte[])
                            {
                                byte[] bytes = (byte[])raw;
                                if (bytes.Length >= 8)
                                {
                                    vram = BitConverter.ToInt64(bytes, 0);
                                }
                            }
                            if (vram <= 0)
                            {
                                continue;
                            }
                            if (best == null || vram > best.MemoryBytes)
                            {
                                GpuDevice gpu = new GpuDevice();
                                gpu.Name = desc.Trim();
                                gpu.MemoryBytes = vram;
                                gpu.Id = "";
                                best = gpu;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return best;
        }

        private static bool IsSoftwareGpu(string name, uint vendorId)
        {
            if (vendorId == 0x1414)
            {
                return true;
            }
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }
            string n = name.ToLowerInvariant();
            return n.IndexOf("basic render", StringComparison.Ordinal) >= 0
                || n.IndexOf("remote desktop", StringComparison.Ordinal) >= 0
                || n.IndexOf("microsoft basic", StringComparison.Ordinal) >= 0;
        }

        private static long ToSignedSize(UIntPtr value)
        {
            ulong raw = value.ToUInt64();
            if (raw > (ulong)long.MaxValue)
            {
                return long.MaxValue;
            }
            return (long)raw;
        }

        [DllImport("dxgi.dll", ExactSpelling = true, EntryPoint = "CreateDXGIFactory")]
        private static extern int NativeCreateDxgiFactory(ref Guid riid, out IDXGIFactory factory);

        [ComImport]
        [Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter
        {
            void SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig]
            int EnumOutputs(uint output, out IntPtr ppOutput);
            void GetDesc(out DXGI_ADAPTER_DESC pDesc);
            [PreserveSig]
            int CheckInterfaceSupport(ref Guid interfaceName, out long pUmdVersion);
        }

        [ComImport]
        [Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory
        {
            void SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig]
            int EnumAdapters(uint adapter, out IDXGIAdapter ppAdapter);
            [PreserveSig]
            int MakeWindowAssociation(IntPtr windowHandle, uint flags);
            [PreserveSig]
            int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig]
            int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
            [PreserveSig]
            int CreateSoftwareAdapter(IntPtr module, out IDXGIAdapter ppAdapter);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public uint AdapterLuidLow;
            public int AdapterLuidHigh;
        }

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetLogicalProcessorInformation")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGetLogicalProcessorInformation(IntPtr buffer, ref uint returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }
    }
}
