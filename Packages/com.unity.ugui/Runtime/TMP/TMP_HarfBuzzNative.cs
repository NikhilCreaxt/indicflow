using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TMPro
{
    internal enum TMP_HBDirection
    {
        LeftToRight = 0,
        RightToLeft = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TMP_HBGlyph
    {
        public uint GlyphId;
        public uint Cluster;
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
    }

    internal sealed class TMP_HBFontHandle : IDisposable
    {
        private IntPtr m_NativeHandle;

        public TMP_HBFontHandle(string fontPath)
        {
            if (string.IsNullOrWhiteSpace(fontPath))
                throw new ArgumentException("Font path is required.", nameof(fontPath));

            FontPath = fontPath;
            byte[] utf8Path = TMP_HarfBuzzNative.ToUtf8NullTerminated(fontPath);

            m_NativeHandle = TMP_HarfBuzzNative.hbhs_create_font_from_file(utf8Path, 0);
            if (m_NativeHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Unable to initialize HarfBuzz font: {fontPath}");

            int upem = TMP_HarfBuzzNative.hbhs_get_upem(m_NativeHandle);
            UnitsPerEm = upem > 0 ? upem : 1000;
        }

        public string FontPath { get; }

        public int UnitsPerEm { get; }

        public bool IsValid => m_NativeHandle != IntPtr.Zero;

        public int Shape(string text, string language, uint scriptTag, TMP_HBDirection direction, TMP_HBGlyph[] outputBuffer)
        {
            if (m_NativeHandle == IntPtr.Zero || outputBuffer == null || outputBuffer.Length == 0)
                return 0;

            byte[] textUtf8 = TMP_HarfBuzzNative.ToUtf8NullTerminated(text ?? string.Empty);
            byte[] languageUtf8 = string.IsNullOrWhiteSpace(language)
                ? TMP_HarfBuzzNative.EmptyUtf8
                : TMP_HarfBuzzNative.ToUtf8NullTerminated(language);

            return TMP_HarfBuzzNative.hbhs_shape(
                m_NativeHandle,
                textUtf8,
                languageUtf8,
                scriptTag,
                (int)direction,
                outputBuffer,
                outputBuffer.Length);
        }

        public void Dispose()
        {
            if (m_NativeHandle != IntPtr.Zero)
            {
                TMP_HarfBuzzNative.hbhs_destroy_font(m_NativeHandle);
                m_NativeHandle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ~TMP_HBFontHandle()
        {
            Dispose();
        }
    }

    internal static class TMP_HarfBuzzNative
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string DllName = "__Internal";
#else
        private const string DllName = "HindiHarfBuzz";
#endif

        internal static readonly byte[] EmptyUtf8 = { 0 };

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hbhs_create_font_from_file(byte[] fontPathUtf8, int faceIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void hbhs_destroy_font(IntPtr fontHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hbhs_get_upem(IntPtr fontHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hbhs_shape(
            IntPtr fontHandle,
            byte[] textUtf8,
            byte[] languageUtf8,
            uint scriptTag,
            int direction,
            [Out] TMP_HBGlyph[] outGlyphs,
            int maxGlyphs);

        internal static uint MakeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length != 4)
                return 0;

            return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
        }

        internal static byte[] ToUtf8NullTerminated(string value)
        {
            if (string.IsNullOrEmpty(value))
                return EmptyUtf8;

            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            byte[] utf8z = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, utf8z, 0, utf8.Length);
            utf8z[utf8z.Length - 1] = 0;
            return utf8z;
        }
    }
}
