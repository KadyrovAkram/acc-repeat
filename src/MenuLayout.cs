using System;
using System.Drawing;

internal static partial class RepeatCorner
{
    private sealed class MenuLayout
    {
        public readonly Size ClientSize;
        public readonly int SignaturePadding;
        public readonly Signature[] Pause, Confirm, Home, SavedTitle, SaveRow;
        public readonly Point QuitButton, ConfirmButton, SavedGamesButton, PlayButtonPosition, DriveButton;
        private readonly double scale, rowSpacing;

        private MenuLayout(Size size, int referenceWidth, double rowSpacing, int signaturePadding,
            Signature[] pause, Signature[] confirm, Signature[] home, Signature[] saves, Signature[] play,
            Point quitButton, Point confirmButton, Point savedGamesButton, Point playButton, Point driveButton)
        {
            ClientSize = size;
            SignaturePadding = signaturePadding;
            scale = (double)size.Width / referenceWidth;
            this.rowSpacing = rowSpacing;
            Pause = pause;
            Confirm = confirm;
            Home = home;
            SavedTitle = new[] { saves[0] };
            SaveRow = new[] { saves[1], play[0] };
            QuitButton = quitButton;
            ConfirmButton = confirmButton;
            SavedGamesButton = savedGamesButton;
            PlayButtonPosition = playButton;
            DriveButton = driveButton;
        }

        public static MenuLayout Select(Size size)
        {
            if (size == new Size(1440, 900) || size == new Size(2880, 1800))
                return new MenuLayout(size, 1440, 61, 0, PracticeMenu, ConfirmQuit, HomeMenu, SavedGames, PlayButton,
                    new Point(719, 641), new Point(597, 514), new Point(570, 666), new Point(1099, 237), new Point(719, 366));
            if (size == new Size(1920, 1080) || size == new Size(2560, 1440))
                return new MenuLayout(size, 1280, 54.25, 4, WidePracticeMenu, WideConfirmQuit, WideHomeMenu, WideSavedGames, WidePlayButton,
                    new Point(639, 512), new Point(530, 416), new Point(505, 556), new Point(973, 171), new Point(639, 294));
            throw new InvalidOperationException(string.Format(
                "Unsupported ACC client size {0} x {1}. Use 1920 x 1080, 2560 x 1440, 1440 x 900, or 2880 x 1800.",
                size.Width, size.Height));
        }

        public int RowOffset(int row)
        {
            return (int)Math.Round(row * rowSpacing, MidpointRounding.AwayFromZero);
        }

        public Point ToClient(Point point)
        {
            return new Point(Scale(point.X), Scale(point.Y));
        }

        public Rectangle CaptureRegion(Signature signature, int yOffset)
        {
            int top = signature.Y + yOffset;
            return Rectangle.FromLTRB(Scale(signature.X - SignaturePadding), Scale(top - SignaturePadding),
                Scale(signature.X + signature.Width + SignaturePadding),
                Scale(top + signature.Height + SignaturePadding));
        }

        private int Scale(int value)
        {
            return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }
    }
}
