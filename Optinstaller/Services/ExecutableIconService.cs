using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Optinstaller.Platform;

namespace Optinstaller.Services;

internal static class ExecutableIconService
{
    public static ExecutableIconBitmap? TryLoadRgbaIcon(string executablePath, int iconSize)
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath) ||
            iconSize <= 0)
        {
            return null;
        }

        var associatedIconBitmap = TryLoadAssociatedIconRgba(executablePath, iconSize);
        if (associatedIconBitmap.HasValue)
        {
            return associatedIconBitmap;
        }

        nint largeIcon = 0;
        nint smallIcon = 0;

        try
        {
            var largeIcons = new nint[1];
            var smallIcons = new nint[1];
            var extractedCount = Win32Native.ExtractIconEx(executablePath, 0, largeIcons, smallIcons, 1);
            if (extractedCount == 0)
            {
                return null;
            }

            largeIcon = largeIcons[0];
            smallIcon = smallIcons[0];

            var iconHandle = largeIcon != 0 ? largeIcon : smallIcon;
            if (iconHandle == 0 || !TryRasterizeIcon(iconHandle, iconSize, out var pixels))
            {
                return null;
            }

            return new ExecutableIconBitmap(pixels, iconSize, iconSize);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (smallIcon != 0 && smallIcon != largeIcon)
            {
                Win32Native.DestroyIcon(smallIcon);
            }

            if (largeIcon != 0)
            {
                Win32Native.DestroyIcon(largeIcon);
            }
        }
    }

    private static ExecutableIconBitmap? TryLoadAssociatedIconRgba(string executablePath, int iconSize)
    {
        try
        {
            using var associatedIcon = Icon.ExtractAssociatedIcon(executablePath);
            if (associatedIcon == null)
            {
                return null;
            }

            using var bitmap = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawIcon(associatedIcon, new Rectangle(0, 0, iconSize, iconSize));
            }

            return TryCopyBitmapToRgba(bitmap, out var pixels)
                ? new ExecutableIconBitmap(pixels, bitmap.Width, bitmap.Height)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static unsafe bool TryRasterizeIcon(nint iconHandle, int iconSize, out byte[] rgbaPixels)
    {
        rgbaPixels = Array.Empty<byte>();
        if (iconHandle == 0 || iconSize <= 0)
        {
            return false;
        }

        var bitmapInfo = new Win32Native.BITMAPINFO
        {
            bmiHeader = new Win32Native.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32Native.BITMAPINFOHEADER>(),
                biWidth = iconSize,
                biHeight = -iconSize,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32Native.BI_RGB,
                biSizeImage = (uint)(iconSize * iconSize * 4),
            },
        };

        var memoryDc = Win32Native.CreateCompatibleDC(0);
        if (memoryDc == 0)
        {
            return false;
        }

        nint dibSection = 0;
        nint oldBitmap = 0;
        try
        {
            dibSection = Win32Native.CreateDIBSection(memoryDc, ref bitmapInfo, Win32Native.DIB_RGB_COLORS, out var dibBits, 0, 0);
            if (dibSection == 0 || dibBits == 0)
            {
                return false;
            }

            oldBitmap = Win32Native.SelectObject(memoryDc, dibSection);
            new Span<byte>((void*)dibBits, iconSize * iconSize * 4).Clear();

            if (!Win32Native.DrawIconEx(memoryDc, 0, 0, iconHandle, iconSize, iconSize, 0, 0, Win32Native.DI_NORMAL))
            {
                return false;
            }

            var bgraPixels = new byte[iconSize * iconSize * 4];
            Marshal.Copy(dibBits, bgraPixels, 0, bgraPixels.Length);
            ConvertBgraToRgbaInPlace(bgraPixels);
            rgbaPixels = bgraPixels;
            return true;
        }
        finally
        {
            if (oldBitmap != 0)
            {
                Win32Native.SelectObject(memoryDc, oldBitmap);
            }

            if (dibSection != 0)
            {
                Win32Native.DeleteObject(dibSection);
            }

            Win32Native.DeleteDC(memoryDc);
        }
    }

    private static bool TryCopyBitmapToRgba(Bitmap bitmap, out byte[] rgbaPixels)
    {
        rgbaPixels = Array.Empty<byte>();

        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData? bitmapData = null;
        try
        {
            bitmapData = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixelBufferLength = Math.Abs(bitmapData.Stride) * bitmap.Height;
            var bgraPixels = new byte[pixelBufferLength];
            Marshal.Copy(bitmapData.Scan0, bgraPixels, 0, bgraPixels.Length);

            if (bitmapData.Stride == bitmap.Width * 4)
            {
                ConvertBgraToRgbaInPlace(bgraPixels);
                rgbaPixels = bgraPixels;
                return true;
            }

            var tightlyPackedPixels = new byte[bitmap.Width * bitmap.Height * 4];
            var sourceRowStride = Math.Abs(bitmapData.Stride);
            for (var row = 0; row < bitmap.Height; row++)
            {
                Buffer.BlockCopy(bgraPixels, row * sourceRowStride, tightlyPackedPixels, row * bitmap.Width * 4, bitmap.Width * 4);
            }

            ConvertBgraToRgbaInPlace(tightlyPackedPixels);
            rgbaPixels = tightlyPackedPixels;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (bitmapData != null)
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
    }

    private static void ConvertBgraToRgbaInPlace(byte[] pixels)
    {
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
        }
    }
}

internal readonly record struct ExecutableIconBitmap(byte[] Pixels, int Width, int Height);
