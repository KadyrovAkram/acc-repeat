using System;
using System.Collections.Generic;
using System.Drawing;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public class MenuLayoutTests
    {
        [Fact]
        public void PauseControlsScaleTo16By9Resolutions()
        {
            var fullHd = MenuLayout.Select(new Size(1920, 1080));
            var quadHd = MenuLayout.Select(new Size(2560, 1440));
            Assert.Equal(new Point(959, 768), fullHd.ToClient(fullHd.QuitButton));
            Assert.Equal(new Point(1278, 1024), quadHd.ToClient(quadHd.QuitButton));
        }

        [Fact]
        public void OriginalLayoutSavedGamesButtonStaysInPlace()
        {
            var original = MenuLayout.Select(new Size(2880, 1800));
            Assert.Equal(new Point(1140, 1332), original.ToClient(original.SavedGamesButton));
        }

        public static IEnumerable<object[]> SupportedSizes()
        {
            yield return new object[] { 1920, 1080 };
            yield return new object[] { 2560, 1440 };
            yield return new object[] { 1440, 900 };
            yield return new object[] { 2880, 1800 };
        }

        [Theory]
        [MemberData(nameof(SupportedSizes))]
        public void EverySavedGamesRowFitsInsideTheClientArea(int width, int height)
        {
            var layout = MenuLayout.Select(new Size(width, height));
            var client = new Rectangle(Point.Empty, layout.ClientSize);
            for (int row = 0; row < VisibleSaveRows; row++)
            {
                int offset = layout.RowOffset(row);
                var play = layout.PlayButtonPosition;
                play.Offset(0, offset);
                Assert.True(client.Contains(layout.ToClient(play)), "Play button of row " + row + " is off screen.");
                Assert.True(client.Contains(layout.CaptureRegion(layout.SaveRow[1], offset)), "Row " + row + " is off screen.");
            }
        }

        [Fact]
        public void UnsupportedResolutionIsRejected()
        {
            Assert.Throws<InvalidOperationException>(() => MenuLayout.Select(new Size(1280, 720)));
        }
    }

    public sealed class MenuSignatureTests : IDisposable
    {
        private const int Padding = 4;
        private static readonly byte[] Mask =
        {
            2, 2, 2, 2, 2,
            2, 1, 2, 1, 2,
            2, 1, 1, 1, 2,
            2, 1, 2, 1, 2,
            2, 2, 2, 2, 2,
        };
        private readonly Signature label = new Signature(0, 0, 5, 5, Convert.ToBase64String(Mask));
        private readonly Bitmap frame;
        private readonly Graphics graphics;

        public MenuSignatureTests()
        {
            frame = new Bitmap(label.Width + 2 * Padding, label.Height + 2 * Padding);
            graphics = Graphics.FromImage(frame);
        }

        public void Dispose()
        {
            graphics.Dispose();
            frame.Dispose();
        }

        [Fact]
        public void BlankRegionDoesNotMatch()
        {
            graphics.Clear(Color.Black);
            Assert.False(label.Matches(frame, 0.70, Padding));
        }

        [Fact]
        public void SolidBrightRegionDoesNotMatch()
        {
            graphics.Clear(Color.White);
            Assert.False(label.Matches(frame, 0.70, Padding));
        }

        [Fact]
        public void SlightlyShiftedLabelStillMatches()
        {
            graphics.Clear(Color.Black);
            for (int y = 0; y < label.Height; y++)
            for (int x = 0; x < label.Width; x++)
                if (Mask[y * label.Width + x] == 1)
                    frame.SetPixel(Padding + x - 2, Padding + y + 3, Color.White);
            Assert.True(label.Matches(frame, 0.70, Padding));
        }
    }
}
