using System.Linq;
using System.Text;
using OpenAT.Formats;
using Xunit;

namespace OpenAT.Tests
{
    public class FormatTests
    {
        static byte[] Latin1(string s) => s.Select(c => (byte)c).ToArray();

        [Fact]
        public void XtRle_RoundTrip()
        {
            var data = Latin1("aaaaaaaabcdefg;;;;;;;;;;;;;;hello\r\nworld" + new string('x', 300));
            var packed = XtRle.Encode(data);
            Assert.True(XtRle.IsPacked(packed));
            Assert.Equal(data, XtRle.Decode(packed));
        }

        [Fact]
        public void XtRle_PassesThroughUnpacked()
        {
            var data = Latin1("plain");
            Assert.Same(data, XtRle.DecodeIfPacked(data));
        }

        [Fact]
        public void TextResource_ParsesGroupsEntriesAndContinuations()
        {
            var src = ">Test\r\n>>1\r\n  first line\r\n  second // comment\r\n>>2\r\n  E::english D::deutsch\r\n>7\r\n>>3\r\n  numeric group\r\n";
            var r = TextResource.Parse(Latin1(src));
            Assert.Equal("first linesecond", r.Get("Test", 1));
            Assert.Equal("english", r.Get("Test", 2));
            Assert.Equal("numeric group", r.Get(7, 3));
            Assert.Null(r.Get("Test", 99));
        }

        [Theory]
        [InlineData("B::umE::twoF::troisD::zwei", TextResource.Language.English, "two")]
        [InlineData("B::umE::twoF::troisD::zwei", TextResource.Language.German, "zwei")]
        [InlineData("B::umE::two F::trois", TextResource.Language.French, "trois")]
        [InlineData("D::nur deutsch", TextResource.Language.English, "nur deutsch")]
        [InlineData("plain text", TextResource.Language.English, "plain text")]
        public void TextResource_SelectsLanguage(string raw, TextResource.Language lang, string expected)
        {
            Assert.Equal(expected, TextResource.SelectLanguage(raw, lang));
        }

        [Theory]
        [InlineData(new byte[] { 1, 0, 0, 0, 0x22 }, TextResource.Language.English)]
        [InlineData(new byte[] { 0, 0, 0, 0 }, TextResource.Language.German)]
        [InlineData(new byte[] { 5, 0, 0, 0 }, TextResource.Language.Dutch)]
        [InlineData(new byte[] { 8, 0, 0, 0 }, TextResource.Language.Portuguese)]
        [InlineData(new byte[] { 42, 0, 0, 0 }, TextResource.Language.English)]
        [InlineData(new byte[0], TextResource.Language.German)]
        public void TextResource_InstallLanguage(byte[] sabbel, TextResource.Language expected)
        {
            Assert.Equal(expected, TextResource.InstallLanguage(sabbel));
        }

        [Fact]
        public void TextResource_DutchAndPortugueseSegments()
        {
            Assert.Equal("nl", TextResource.SelectLanguage("E::en N::nl O::pt", TextResource.Language.Dutch));
            Assert.Equal("pt", TextResource.SelectLanguage("E::en N::nl O::pt", TextResource.Language.Portuguese));
        }

        [Fact]
        public void CsvTable_ParsesSemicolons()
        {
            var t = CsvTable.Parse(XtRle.Encode(Latin1("a;b;c\r\n1;;2;3\r\n\r\n4;5\r")));
            Assert.Equal(2, t.Rows.Count);           // header skipped, blank lines dropped
            Assert.Equal("3", t.Rows[0][2]);          // ";;" collapses (strtok)
            Assert.Equal(-52, CsvTable.Atoi("-52,2"));
            Assert.Equal(11, CsvTable.Atoi("11. Mrz"));
            Assert.Equal(1.0, CsvTable.Atof("1,1"));
            Assert.Equal(0.8, CsvTable.Atof(".8"), 6);
            Assert.Equal(0, CsvTable.Atoi("x"));
            Assert.Equal("c;d", CsvTable.RestOfLine("a;b;c;d", 2));
        }

        [Fact]
        public void Rgb565_ExpandsToFullRange()
        {
            Assert.Equal(0xFFFFFFFFu, Bitmap16.ToRgba32(0xFFFF));
            Assert.Equal(0xFF000000u, Bitmap16.ToRgba32(0x0000));
            Assert.Equal(0xFF0000FFu, Bitmap16.ToRgba32(0xF800));
        }

        [Fact]
        public void RealInstall_AllAssetsParse()
        {
            var install = GameInstall.TryLocate();
            if (install == null) return; // no game files on this machine
            foreach (var f in install.List("room", ".gli"))
            {
                var lib = new GliLibrary(install.ReadAllBytes(f), f);
                Assert.NotEmpty(lib.Entries);
            }
            var std = TextResource.Parse(install.ReadAllBytes("misc/std_ger.res"));
            Assert.True(std.Count > 1000);
            var font = McfFont.Parse(install.ReadAllBytes("misc/NORMFONT.MCF"));
            Assert.True(font.Advance('A') > 0);
        }
    }
}
