using System.Buffers.Binary;
using System.Text;

const string RtssExportName = "RTSSHooksCompatibility";

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RtssExportPatcher <path-to-apphost-exe>");
    return 1;
}

var targetPath = Path.GetFullPath(args[0]);
if (!File.Exists(targetPath))
{
    Console.Error.WriteLine($"File not found: {targetPath}");
    return 1;
}

try
{
    var image = File.ReadAllBytes(targetPath);
    var pe = PeImage.Load(image, targetPath);

    if (pe.HasExportTable)
    {
        if (pe.HasExport(RtssExportName))
        {
            Console.WriteLine($"RTSS export already present: {targetPath}");
            return 0;
        }

        throw new InvalidOperationException($"{Path.GetFileName(targetPath)} already contains an export table and cannot be patched safely.");
    }

    var patch = RtssExportPatch.Create(pe, Path.GetFileName(targetPath));
    var patchedImage = patch.Apply(image, pe);

    File.WriteAllBytes(targetPath, patchedImage);
    Console.WriteLine($"Injected RTSS export into {targetPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

internal sealed class RtssExportPatch
{
    private const uint ImageScnCntInitializedData = 0x00000040;
    private const uint ImageScnMemRead = 0x40000000;
    private const uint ImageScnMemWrite = 0x80000000;

    private readonly byte[] _sectionData;
    private readonly uint _virtualSize;

    private RtssExportPatch(byte[] sectionData, uint virtualSize)
    {
        _sectionData = sectionData;
        _virtualSize = virtualSize;
    }

    public static RtssExportPatch Create(PeImage pe, string moduleName)
    {
        var exportNameBytes = Encoding.ASCII.GetBytes("RTSSHooksCompatibility\0");
        var moduleNameBytes = Encoding.ASCII.GetBytes(moduleName + "\0");

        const int exportDirectorySize = 40;
        var exportAddressTableOffset = Align(exportDirectorySize, 4);
        var exportNamePointerTableOffset = exportAddressTableOffset + 4;
        var exportOrdinalTableOffset = exportNamePointerTableOffset + 4;
        var exportNameOffset = Align(exportOrdinalTableOffset + 2, 4);
        var moduleNameOffset = Align(exportNameOffset + exportNameBytes.Length, 4);
        var exportedValueOffset = Align(moduleNameOffset + moduleNameBytes.Length, 4);
        var virtualSize = (uint)(exportedValueOffset + sizeof(uint));
        var rawSize = Align((int)virtualSize, (int)pe.FileAlignment);

        var sectionData = new byte[rawSize];

        var exportDirectoryRva = pe.NewSectionRva;
        var exportAddressTableRva = exportDirectoryRva + (uint)exportAddressTableOffset;
        var exportNamePointerTableRva = exportDirectoryRva + (uint)exportNamePointerTableOffset;
        var exportOrdinalTableRva = exportDirectoryRva + (uint)exportOrdinalTableOffset;
        var exportNameRva = exportDirectoryRva + (uint)exportNameOffset;
        var moduleNameRva = exportDirectoryRva + (uint)moduleNameOffset;
        var exportedValueRva = exportDirectoryRva + (uint)exportedValueOffset;

        WriteUInt32(sectionData, 12, moduleNameRva);
        WriteUInt32(sectionData, 16, 1);
        WriteUInt32(sectionData, 20, 1);
        WriteUInt32(sectionData, 24, 1);
        WriteUInt32(sectionData, 28, exportAddressTableRva);
        WriteUInt32(sectionData, 32, exportNamePointerTableRva);
        WriteUInt32(sectionData, 36, exportOrdinalTableRva);

        WriteUInt32(sectionData, exportAddressTableOffset, exportedValueRva);
        WriteUInt32(sectionData, exportNamePointerTableOffset, exportNameRva);
        WriteUInt16(sectionData, exportOrdinalTableOffset, 0);

        exportNameBytes.CopyTo(sectionData.AsSpan(exportNameOffset));
        moduleNameBytes.CopyTo(sectionData.AsSpan(moduleNameOffset));

        return new RtssExportPatch(sectionData, virtualSize);
    }

    public byte[] Apply(byte[] image, PeImage pe)
    {
        var requiredLength = checked((int)(pe.NewSectionRawPointer + (uint)_sectionData.Length));
        var patched = new byte[Math.Max(image.Length, requiredLength)];
        image.CopyTo(patched, 0);
        _sectionData.CopyTo(patched, checked((int)pe.NewSectionRawPointer));

        WriteSectionHeader(patched, pe.NewSectionHeaderOffset, _virtualSize, pe.NewSectionRva, (uint)_sectionData.Length, pe.NewSectionRawPointer);

        WriteUInt16(patched, pe.NumberOfSectionsOffset, (ushort)(pe.NumberOfSections + 1));
        WriteUInt32(patched, pe.SizeOfImageOffset, Align(pe.NewSectionRva + _virtualSize, pe.SectionAlignment));
        WriteUInt32(patched, pe.ExportTableDirectoryOffset, pe.NewSectionRva);
        WriteUInt32(patched, pe.ExportTableDirectoryOffset + 4, _virtualSize);

        return patched;
    }

    private static void WriteSectionHeader(byte[] image, int offset, uint virtualSize, uint virtualAddress, uint rawSize, uint rawPointer)
    {
        var name = Encoding.ASCII.GetBytes(".rtss\0\0\0");
        name.CopyTo(image, offset);
        WriteUInt32(image, offset + 8, virtualSize);
        WriteUInt32(image, offset + 12, virtualAddress);
        WriteUInt32(image, offset + 16, rawSize);
        WriteUInt32(image, offset + 20, rawPointer);
        WriteUInt32(image, offset + 24, 0);
        WriteUInt32(image, offset + 28, 0);
        WriteUInt16(image, offset + 32, 0);
        WriteUInt16(image, offset + 34, 0);
        WriteUInt32(image, offset + 36, ImageScnCntInitializedData | ImageScnMemRead | ImageScnMemWrite);
    }

    private static int Align(int value, int alignment)
    {
        var mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    private static uint Align(uint value, uint alignment)
    {
        var mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    private static void WriteUInt16(byte[] image, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset, sizeof(ushort)), value);
    }

    private static void WriteUInt32(byte[] image, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, sizeof(uint)), value);
    }
}

internal sealed class PeImage
{
    private const ushort DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;
    private const int FileHeaderSize = 20;
    private const int SectionHeaderSize = 40;

    private readonly byte[] _image;
    private readonly List<Section> _sections;

    private PeImage(
        byte[] image,
        int numberOfSectionsOffset,
        int sizeOfImageOffset,
        int exportTableDirectoryOffset,
        uint sectionAlignment,
        uint fileAlignment,
        ushort numberOfSections,
        int newSectionHeaderOffset,
        uint newSectionRva,
        uint newSectionRawPointer,
        List<Section> sections)
    {
        _image = image;
        NumberOfSectionsOffset = numberOfSectionsOffset;
        SizeOfImageOffset = sizeOfImageOffset;
        ExportTableDirectoryOffset = exportTableDirectoryOffset;
        SectionAlignment = sectionAlignment;
        FileAlignment = fileAlignment;
        NumberOfSections = numberOfSections;
        NewSectionHeaderOffset = newSectionHeaderOffset;
        NewSectionRva = newSectionRva;
        NewSectionRawPointer = newSectionRawPointer;
        _sections = sections;
    }

    public int NumberOfSectionsOffset { get; }

    public int SizeOfImageOffset { get; }

    public int ExportTableDirectoryOffset { get; }

    public uint SectionAlignment { get; }

    public uint FileAlignment { get; }

    public ushort NumberOfSections { get; }

    public int NewSectionHeaderOffset { get; }

    public uint NewSectionRva { get; }

    public uint NewSectionRawPointer { get; }

    public bool HasExportTable => ExportTableRva != 0 && ExportTableSize != 0;

    private uint ExportTableRva => ReadUInt32(_image, ExportTableDirectoryOffset);

    private uint ExportTableSize => ReadUInt32(_image, ExportTableDirectoryOffset + 4);

    public static PeImage Load(byte[] image, string path)
    {
        if (image.Length < 512)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is too small to be a PE image.");
        }

        if (ReadUInt16(image, 0) != DosSignature)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is not an MZ executable.");
        }

        var peOffset = checked((int)ReadUInt32(image, 0x3C));
        if (peOffset <= 0 || peOffset + 4 + FileHeaderSize > image.Length)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} has an invalid PE header offset.");
        }

        if (ReadUInt32(image, peOffset) != PeSignature)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is missing the PE signature.");
        }

        var fileHeaderOffset = peOffset + 4;
        var numberOfSectionsOffset = fileHeaderOffset + 2;
        var numberOfSections = ReadUInt16(image, numberOfSectionsOffset);
        var sizeOfOptionalHeader = ReadUInt16(image, fileHeaderOffset + 16);
        var optionalHeaderOffset = fileHeaderOffset + FileHeaderSize;
        if (optionalHeaderOffset + sizeOfOptionalHeader > image.Length)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} has an invalid optional header.");
        }

        var magic = ReadUInt16(image, optionalHeaderOffset);
        var dataDirectoryOffset = magic switch
        {
            Pe32Magic => optionalHeaderOffset + 96,
            Pe32PlusMagic => optionalHeaderOffset + 112,
            _ => throw new InvalidOperationException($"{Path.GetFileName(path)} is not a supported PE image.")
        };

        var sectionAlignment = ReadUInt32(image, optionalHeaderOffset + 32);
        var fileAlignment = ReadUInt32(image, optionalHeaderOffset + 36);
        var sizeOfImageOffset = optionalHeaderOffset + 56;
        var exportTableDirectoryOffset = dataDirectoryOffset;
        var sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;

        var sections = new List<Section>(numberOfSections);
        var firstSectionRawPointer = int.MaxValue;
        for (var index = 0; index < numberOfSections; index++)
        {
            var sectionOffset = sectionTableOffset + (index * SectionHeaderSize);
            if (sectionOffset + SectionHeaderSize > image.Length)
            {
                throw new InvalidOperationException($"{Path.GetFileName(path)} has a truncated section table.");
            }

            var section = new Section(
                ReadUInt32(image, sectionOffset + 8),
                ReadUInt32(image, sectionOffset + 12),
                ReadUInt32(image, sectionOffset + 16),
                ReadUInt32(image, sectionOffset + 20));
            sections.Add(section);

            if (section.RawPointer != 0)
            {
                firstSectionRawPointer = Math.Min(firstSectionRawPointer, checked((int)section.RawPointer));
            }
        }

        if (firstSectionRawPointer == int.MaxValue)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} does not contain any mapped sections.");
        }

        var newSectionHeaderOffset = sectionTableOffset + (numberOfSections * SectionHeaderSize);
        if (newSectionHeaderOffset + SectionHeaderSize > firstSectionRawPointer)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} does not have enough header space for an additional section.");
        }

        var lastSection = sections[^1];
        var newSectionRva = Align(lastSection.VirtualAddress + Math.Max(lastSection.VirtualSize, lastSection.RawSize), sectionAlignment);
        var newSectionRawPointer = Align(lastSection.RawPointer + lastSection.RawSize, fileAlignment);

        return new PeImage(
            image,
            numberOfSectionsOffset,
            sizeOfImageOffset,
            exportTableDirectoryOffset,
            sectionAlignment,
            fileAlignment,
            numberOfSections,
            newSectionHeaderOffset,
            newSectionRva,
            newSectionRawPointer,
            sections);
    }

    public bool HasExport(string exportName)
    {
        if (!HasExportTable)
        {
            return false;
        }

        var exportDirectoryOffset = RvaToOffset(ExportTableRva);
        if (exportDirectoryOffset < 0 || exportDirectoryOffset + 40 > _image.Length)
        {
            return false;
        }

        var numberOfNames = ReadUInt32(_image, exportDirectoryOffset + 24);
        var addressOfNamesRva = ReadUInt32(_image, exportDirectoryOffset + 32);
        var addressOfNamesOffset = RvaToOffset(addressOfNamesRva);
        if (numberOfNames == 0 || addressOfNamesOffset < 0)
        {
            return false;
        }

        for (var index = 0u; index < numberOfNames; index++)
        {
            var entryOffset = addressOfNamesOffset + checked((int)(index * sizeof(uint)));
            if (entryOffset + sizeof(uint) > _image.Length)
            {
                return false;
            }

            var nameRva = ReadUInt32(_image, entryOffset);
            var nameOffset = RvaToOffset(nameRva);
            if (nameOffset < 0)
            {
                continue;
            }

            if (string.Equals(ReadAsciiString(nameOffset), exportName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private int RvaToOffset(uint rva)
    {
        foreach (var section in _sections)
        {
            var sectionLength = Math.Max(section.VirtualSize, section.RawSize);
            if (rva < section.VirtualAddress || rva >= section.VirtualAddress + sectionLength)
            {
                continue;
            }

            return checked((int)(section.RawPointer + (rva - section.VirtualAddress)));
        }

        return -1;
    }

    private string ReadAsciiString(int offset)
    {
        var end = offset;
        while (end < _image.Length && _image[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(_image, offset, end - offset);
    }

    private static ushort ReadUInt16(byte[] image, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] image, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, sizeof(uint)));
    }

    private static uint Align(uint value, uint alignment)
    {
        var mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    private sealed record Section(uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawPointer);
}
